using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using wa_api.Common.Errors;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.RateLimiting;

namespace wa_api.Features.Messages;

/// <summary>
/// The single outbound WhatsApp send pathway (see <see cref="IWhatsAppMessageSender"/>). Extracted from
/// the former <c>MessageService.SendAsync</c> so the standalone endpoint and the conversation endpoint
/// both flow through identical rate-limiting, Meta API, persistence, and metering.
/// </summary>
public class WhatsAppMessageSender(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IWabaRateLimiter rateLimiter,
    IOptions<RateLimitOptions> rateOptions,
    wa_api.Common.Subscriptions.ISubscriptionMeter meter,
    ILogger<WhatsAppMessageSender> logger)
    : IWhatsAppMessageSender
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly RateLimitOptions _rateOptions = rateOptions.Value;

    public Task<Message> SendAsync(
        Contact contact, WabaConnection waba, string body, Guid? conversationId,
        bool chargeCredit, CancellationToken ct = default)
        => SendCoreAsync(
            contact, waba, body, conversationId, chargeCredit,
            () => CallMetaApiAsync(waba.PhoneNumberId, waba.EncryptedAccessToken, contact.Phone, body, ct),
            shape: null, ct);

    public Task<Message> SendMediaAsync(
        Contact contact, WabaConnection waba, string mediaType, string metaMediaId, string mimeType,
        string? fileName, string? caption, Guid? conversationId, bool chargeCredit, CancellationToken ct = default)
    {
        // Audio has no caption on WhatsApp. The placeholder body mirrors what inbound media stores, which the
        // inbox already knows to hide under the media.
        var cap = mediaType == "audio" || string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        var body = cap ?? $"[{mediaType}]";

        var inner = new Dictionary<string, object?> { ["id"] = metaMediaId };
        if (cap is not null) inner["caption"] = cap;
        if (mediaType == "document" && !string.IsNullOrWhiteSpace(fileName)) inner["filename"] = fileName;

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = contact.Phone,
            ["type"] = mediaType,
            [mediaType] = inner,
        };

        return SendCoreAsync(
            contact, waba, body, conversationId, chargeCredit,
            () => PostToMetaAsync(waba.PhoneNumberId, waba.EncryptedAccessToken, payload, ct),
            shape: m =>
            {
                m.MediaType = mediaType;
                m.MediaMimeType = mimeType;
                m.MediaFileName = fileName;
                m.MetaMediaId = metaMediaId;
            },
            ct);
    }

    /// <summary>
    /// The shared send pipeline: pace → reserve quota → call Meta → persist → refund on failure. Text and
    /// media differ only in the Meta call and in what they stamp on the row.
    /// </summary>
    private async Task<Message> SendCoreAsync(
        Contact contact, WabaConnection waba, string body, Guid? conversationId, bool chargeCredit,
        Func<Task<(string? ExternalId, string? Error)>> callMeta, Action<Message>? shape, CancellationToken ct)
    {
        // Session/service reply (within the 24h window): per-second pacing only, no tier consumption.
        await rateLimiter.AcquireOrThrowAsync(
            waba.PhoneNumberId, recipientContactId: null, dailyTierLimit: 0,
            maxWait: TimeSpan.FromMilliseconds(_rateOptions.InteractiveMaxWaitMs), ct);

        // Reserve quota atomically BEFORE sending (C1 reserve-then-send): the meter enforces a hard ceiling,
        // so concurrent sends near the cap can't overshoot the plan. Null ⇒ ceiling reached (the caller's
        // active-subscription gate has already ensured a sub exists). Refunded below if the send then fails.
        // A free in-window reply skips this entirely — no reservation, so nothing to refund and no ceiling
        // to hit, which is what lets an agent keep replying on an exhausted balance.
        Guid? reservedSubId = null;
        if (chargeCredit)
        {
            reservedSubId = await meter.TryReserveAsync(contact.CompanyId, 1, ct);
            if (reservedSubId is null)
                throw new BusinessRuleException("QUOTA_EXCEEDED", "Your message quota has been reached.");
        }

        var (externalId, errorMessage) = await callMeta();

        var message = new Message
        {
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            ConversationId = conversationId,
            Body = body,
            Direction = MessageDirection.Outbound,
            // Accepted (not Sent) on success: Meta returned a wamid but its 'sent' status webhook is still
            // in flight. The webhook promotes Accepted → Sent; a failed API call is terminal Failed here.
            Status = externalId is not null ? MessageStatus.Accepted : MessageStatus.Failed,
            ExternalMessageId = externalId,
            ErrorMessage = errorMessage,
            // Stamp the reserved subscription so a later delivery-failure webhook refunds that exact row.
            MeteredSubscriptionId = reservedSubId,
            // CompanyId is auto-stamped by AuditInterceptor from the JWT tenant context (HTTP path).
        };
        shape?.Invoke(message);
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        // Send failed at the API level — give the reservation back and drop the (unused) metered stamp.
        // Nothing to give back on a free send, which never reserved.
        if (externalId is null && reservedSubId is not null)
        {
            await meter.RefundAsync(reservedSubId.Value, 1, ct);
            message.MeteredSubscriptionId = null;
            await db.SaveChangesAsync(ct);
        }

        return message;
    }

    public async Task<Message> SendTemplateAsync(
        Contact contact, WabaConnection waba, string templateName, string language,
        IReadOnlyList<string> parameters, string renderedBody, Guid? conversationId,
        CancellationToken ct = default)
    {
        // Business-initiated, so the daily tier applies as well as the per-second pace: Meta caps how
        // many DIFFERENT customers a number may start a conversation with in 24 hours.
        await rateLimiter.AcquireOrThrowAsync(
            waba.PhoneNumberId, contact.Id, TierLimit(waba.MessagingTier),
            maxWait: TimeSpan.FromMilliseconds(_rateOptions.InteractiveMaxWaitMs), ct);

        // Reserve BEFORE sending, refund below if Meta refuses. A template always costs — there is no
        // free-window case here, which is the whole reason this path is separate.
        var reservedSubId = await meter.TryReserveAsync(contact.CompanyId, 1, ct);
        if (reservedSubId is null)
            throw new BusinessRuleException("QUOTA_EXCEEDED", "Your message quota has been reached.");

        var (externalId, errorMessage) = await CallMetaTemplateApiAsync(
            waba.PhoneNumberId, waba.EncryptedAccessToken, contact.Phone,
            templateName, language, parameters, ct);

        var message = new Message
        {
            CompanyId = contact.CompanyId,
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            ConversationId = conversationId,
            // The rendered text, not the template body: an agent opening the thread has to see what the
            // customer saw.
            Body = renderedBody,
            Direction = MessageDirection.Outbound,
            Status = externalId is not null ? MessageStatus.Accepted : MessageStatus.Failed,
            ExternalMessageId = externalId,
            ErrorMessage = errorMessage,
            MeteredSubscriptionId = reservedSubId,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        if (externalId is null)
        {
            await meter.RefundAsync(reservedSubId.Value, 1, ct);
            message.MeteredSubscriptionId = null;
            await db.SaveChangesAsync(ct);
        }

        return message;
    }

    /// <summary>
    /// How many distinct customers this number may start a conversation with per day. Meta's tiers.
    /// </summary>
    private static int TierLimit(MessagingTier tier) => tier switch
    {
        MessagingTier.Tier250 => 250,
        MessagingTier.Tier1K => 1_000,
        MessagingTier.Tier10K => 10_000,
        MessagingTier.Tier100K => 100_000,
        _ => 0,                       // Unlimited / unknown: no local ceiling, Meta still has the say
    };

    private async Task<(string? ExternalId, string? Error)> CallMetaTemplateApiAsync(
        string phoneNumberId, string accessToken, string toPhone, string templateName, string language,
        IReadOnlyList<string> parameters, CancellationToken ct)
    {
        var components = parameters.Count == 0
            ? null
            : new object[]
            {
                new
                {
                    type = "body",
                    parameters = parameters.Select(p => new { type = "text", text = p }).ToArray(),
                },
            };

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components,
            },
        };

        return await PostToMetaAsync(phoneNumberId, accessToken, payload, ct);
    }

    /// <summary>
    /// Bounded well under the shortest human-pace delay: a slow Meta call must never be the reason the
    /// reply itself is late.
    /// </summary>
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(3);

    public async Task ShowTypingAsync(WabaConnection waba, string? inboundWamid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inboundWamid)) return;

        var client = httpClientFactory.CreateClient("MetaGraph");
        var payload = new
        {
            messaging_product = "whatsapp",
            status = "read",
            message_id = inboundWamid,
            typing_indicator = new { type = "text" },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, $"v19.0/{waba.PhoneNumberId}/messages") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", waba.EncryptedAccessToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TypingTimeout);

        try
        {
            using var res = await client.SendAsync(req, timeout.Token);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Typing indicator rejected by Meta ({Status}) for {Wamid}: {Body}",
                    (int)res.StatusCode, inboundWamid, await res.Content.ReadAsStringAsync(CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Typing indicator timed out for {Wamid}.", inboundWamid);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Typing indicator failed for {Wamid}.", inboundWamid);
        }
    }

    private async Task<(string? ExternalId, string? Error)> CallMetaApiAsync(
        string phoneNumberId, string accessToken, string toPhone, string body, CancellationToken ct)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "text",
            // Without preview_url Meta sends a bare link; with it the customer gets the thumbnail card
            // they expect from WhatsApp. Only asked for when there is a link to preview.
            text = new { body, preview_url = ContainsWebLink(body) }
        };

        return await PostToMetaAsync(phoneNumberId, accessToken, payload, ct);
    }

    /// <summary>
    /// POSTs one message payload and reads back either the wamid or Meta's own error message. Shared by
    /// the text and template paths, which differ only in what they put in the body.
    /// </summary>
    private async Task<(string? ExternalId, string? Error)> PostToMetaAsync(
        string phoneNumberId, string accessToken, object payload, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");

        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, $"v19.0/{phoneNumberId}/messages") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var msgId = doc.RootElement
                    .GetProperty("messages")[0]
                    .GetProperty("id")
                    .GetString();
                return (msgId, null);
            }

            // Extract Meta error message for logging / surfacing to the caller.
            string? metaError = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                metaError = doc.RootElement
                    .GetProperty("error")
                    .GetProperty("message")
                    .GetString();
            }
            catch { /* swallow parse failures */ }

            // 131047 = outside the 24-hour customer service window (the conversation endpoint pre-checks
            // WindowExpiresAt to avoid this round-trip, but this remains the authoritative backstop).
            logger.LogWarning("Meta API returned {Status}: {Error}", (int)res.StatusCode, metaError);
            return (null, metaError ?? $"Meta API error {(int)res.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Meta Graph API");
            return (null, "Could not reach WhatsApp API. Please try again.");
        }
    }

    private static bool ContainsWebLink(string body) =>
        body.Contains("https://", StringComparison.OrdinalIgnoreCase)
        || body.Contains("http://", StringComparison.OrdinalIgnoreCase);
}
