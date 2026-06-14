using Microsoft.EntityFrameworkCore;
using wa_api.Features.Conversations;
using wa_api.Features.Messages.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages;

/// <summary>
/// Read model for outbound message history (GET /messages) plus the legacy standalone send endpoint.
/// Sending now delegates to <see cref="IConversationService"/> so a standalone message is attached to
/// the contact's open conversation and shows up in the inbox — there is one send pathway, not two.
/// </summary>
public class MessageService(AppDbContext db, IConversationService conversations) : IMessageService
{
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

    /// <summary>
    /// POST /messages — send a standalone message to a contact. Find-or-creates the contact's open
    /// conversation, sends through the shared pathway, and updates the thread snapshot. Throws
    /// WHATSAPP_SEND_FAILED (after persisting the failed row) when Meta rejects the send.
    /// </summary>
    public Task<MessageResponse> SendAsync(SendMessageRequest request, CancellationToken ct = default)
        => conversations.SendToContactAsync(request.ContactId, request.WabaConnectionId, request.Body, ct);
}
