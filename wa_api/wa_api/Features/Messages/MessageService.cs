using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.RateLimiting;

namespace wa_api.Features.Messages;

public class MessageService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IWabaRateLimiter rateLimiter,
    IOptions<RateLimitOptions> rateOptions,
    ISubscriptionGate subscriptionGate,
    ILogger<MessageService> logger)
    : IMessageService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly RateLimitOptions _rateOptions = rateOptions.Value;

    public async Task<(IReadOnlyList<MessageResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Messages.AsNoTracking()
            .Include(m => m.Contact)
            .Include(m => m.WabaConnection)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(m =>
                EF.Functions.ILike(m.Contact.Name, term) ||
                EF.Functions.ILike(m.Contact.Phone, term) ||
                EF.Functions.ILike(m.Body, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = sortBy?.ToLowerInvariant() switch
        {
            "contact" => desc ? query.OrderByDescending(m => m.Contact.Name) : query.OrderBy(m => m.Contact.Name),
            "status" => desc ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            _ => desc ? query.OrderByDescending(m => m.CreatedAt) : query.OrderBy(m => m.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MessageResponse(
                m.Id, m.ContactId, m.Contact.Name, m.Contact.Phone,
                m.WabaConnectionId, m.WabaConnection.DisplayPhoneNumber,
                m.Body, m.Direction, m.Status, m.ExternalMessageId, m.ErrorMessage, m.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<MessageResponse> SendAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        // Enforce the caller company's active, in-quota subscription BEFORE any send work.
        // Throws SUBSCRIPTION_INACTIVE / QUOTA_EXCEEDED; SuperAdmin bypasses inside the gate.
        await subscriptionGate.EnsureCanSendAsync(ct);

        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new NotFoundException("Contact", request.ContactId);

        var waba = await db.WabaConnections.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", request.WabaConnectionId);

        if (!waba.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The selected WhatsApp connection is inactive.");

        // ── Anti-ban guards (this endpoint sends free-form text, NOT a template) ──────────────
        // Free-form is only permitted to a reachable contact inside an open 24-hour customer-service
        // window. Sending outside the window is a policy violation (Meta 131047) that hurts the number's
        // quality rating; pre-checking turns a quality-damaging round-trip into a clean local error.
        // For business-initiated outreach (cold contacts / closed window) callers must use a campaign template.
        if (!contact.IsWhatsAppValid)
            throw new BusinessRuleException("NOT_ON_WHATSAPP",
                "This contact is not a WhatsApp user, so the message can't be delivered.");

        var windowExpiresAt = await db.Conversations
            .Where(c => c.ContactId == contact.Id
                     && c.WabaConnectionId == waba.Id
                     && c.Status == ConversationStatus.Open)
            .Select(c => c.WindowExpiresAt)
            .FirstOrDefaultAsync(ct);

        if (windowExpiresAt is null || windowExpiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("WINDOW_CLOSED",
                "The 24-hour customer service window is closed for this contact. Reach them with an "
                + "approved template via a campaign instead.");

        // Standalone send is a session message (free-form text): per-second pacing only, no tier consumption.
        await rateLimiter.AcquireOrThrowAsync(
            waba.PhoneNumberId, recipientContactId: null, dailyTierLimit: 0,
            maxWait: TimeSpan.FromMilliseconds(_rateOptions.InteractiveMaxWaitMs), ct);

        var (externalId, errorMessage) = await CallMetaApiAsync(waba.PhoneNumberId, waba.EncryptedAccessToken, contact.Phone, request.Body, ct);

        var message = new Message
        {
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            Body = request.Body,
            Status = externalId is not null ? MessageStatus.Sent : MessageStatus.Failed,
            ExternalMessageId = externalId,
            ErrorMessage = errorMessage,
            // CompanyId is auto-stamped by AuditInterceptor from JWT tenant context.
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        if (message.Status == MessageStatus.Failed)
            throw new BusinessRuleException("WHATSAPP_SEND_FAILED", errorMessage ?? "Failed to send message via WhatsApp.");

        // Best-effort quota metering against the active subscription (PRD 3.1 gate stub).
        // The authoritative per-message metering arrives with the Phase 6 send pipeline.
        await IncrementSubscriptionUsageAsync(ct);

        return Map(message, contact.Name, contact.Phone, waba.DisplayPhoneNumber);
    }

    /// <summary>
    /// Atomically increments the caller company's active-subscription usage counter by one. The target
    /// row is resolved first (the EF global query filter scopes it to the JWT company; the unique
    /// active-subscription index guarantees at most one), then bumped with a single
    /// <c>UPDATE … SET col = col + 1 WHERE Id = …</c>. Scoping the update to that one Id keeps it
    /// single-row even on the SuperAdmin plane (where the query filter is bypassed), so it can never
    /// amplify across tenants — while <c>col = col + 1</c> stays lost-update-safe under concurrency.
    /// No-op when no active subscription exists. ExecuteUpdate bypasses the audit interceptor, so
    /// UpdatedAt is set explicitly here.
    /// </summary>
    private async Task IncrementSubscriptionUsageAsync(CancellationToken ct)
    {
        var subId = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (subId is null) return;

        var now = DateTime.UtcNow;
        await db.Subscriptions
            .Where(s => s.Id == subId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MessagesUsedThisPeriod, x => x.MessagesUsedThisPeriod + 1)
                .SetProperty(x => x.UpdatedAt, now), ct);
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

            // 131047 = outside the 24-hour customer service window.
            logger.LogWarning("Meta API returned {Status}: {Error}", (int)res.StatusCode, metaError);
            return (null, metaError ?? $"Meta API error {(int)res.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Meta Graph API");
            return (null, "Could not reach WhatsApp API. Please try again.");
        }
    }

    private static MessageResponse Map(Message m, string contactName, string contactPhone, string displayPhoneNumber)
        => new(m.Id, m.ContactId, contactName, contactPhone, m.WabaConnectionId, displayPhoneNumber,
               m.Body, m.Direction, m.Status, m.ExternalMessageId, m.ErrorMessage, m.CreatedAt);
}
