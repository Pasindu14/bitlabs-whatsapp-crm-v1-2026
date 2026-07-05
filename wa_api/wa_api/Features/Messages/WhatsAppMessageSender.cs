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

    public async Task<Message> SendAsync(
        Contact contact, WabaConnection waba, string body, Guid? conversationId,
        CancellationToken ct = default)
    {
        // Session/service reply (within the 24h window): per-second pacing only, no tier consumption.
        await rateLimiter.AcquireOrThrowAsync(
            waba.PhoneNumberId, recipientContactId: null, dailyTierLimit: 0,
            maxWait: TimeSpan.FromMilliseconds(_rateOptions.InteractiveMaxWaitMs), ct);

        // Reserve quota atomically BEFORE sending (C1 reserve-then-send): the meter enforces a hard ceiling,
        // so concurrent sends near the cap can't overshoot the plan. Null ⇒ ceiling reached (the caller's
        // active-subscription gate has already ensured a sub exists). Refunded below if the send then fails.
        var reservedSubId = await meter.TryReserveAsync(contact.CompanyId, 1, ct);
        if (reservedSubId is null)
            throw new BusinessRuleException("QUOTA_EXCEEDED", "Your message quota has been reached.");

        var (externalId, errorMessage) = await CallMetaApiAsync(
            waba.PhoneNumberId, waba.EncryptedAccessToken, contact.Phone, body, ct);

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
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        // Send failed at the API level — give the reservation back and drop the (unused) metered stamp.
        if (externalId is null)
        {
            await meter.RefundAsync(reservedSubId.Value, 1, ct);
            message.MeteredSubscriptionId = null;
            await db.SaveChangesAsync(ct);
        }

        return message;
    }

    private async Task<(string? ExternalId, string? Error)> CallMetaApiAsync(
        string phoneNumberId, string accessToken, string toPhone, string body, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "text",
            text = new { body }
        };

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
}
