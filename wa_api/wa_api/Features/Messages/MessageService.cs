using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Features.Messages.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages;

public class MessageService(AppDbContext db, IHttpClientFactory httpClientFactory, ILogger<MessageService> logger)
    : IMessageService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
            "status"  => desc ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            _         => desc ? query.OrderByDescending(m => m.CreatedAt) : query.OrderBy(m => m.CreatedAt),
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
        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new NotFoundException("Contact", request.ContactId);

        var waba = await db.WabaConnections.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", request.WabaConnectionId);

        if (!waba.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The selected WhatsApp connection is inactive.");

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

        return Map(message, contact.Name, contact.Phone, waba.DisplayPhoneNumber);
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
