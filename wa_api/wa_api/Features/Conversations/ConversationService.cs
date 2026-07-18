using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Conversations;

/// <summary>
/// Owns the conversation inbox + the single outbound send pathway (used by both the conversation reply
/// endpoint and the standalone /messages adapter). HTTP-scoped: relies on the tenant query filter for
/// isolation and on <c>AuditInterceptor</c> to stamp <c>CompanyId</c>. The inbound (webhook) path is
/// deliberately NOT here — it runs with no tenant context and is handled in InboundMessageWebhookHandler.
/// </summary>
public class ConversationService(
    AppDbContext db,
    IWhatsAppMessageSender sender,
    IChatNotifier notifier) : IConversationService
{
    /// <summary>Max preview length stored on the conversation snapshot.</summary>
    private const int PreviewLength = 200;

    public async Task<(IReadOnlyList<ConversationResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Conversations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Contact.Name, term) ||
                EF.Functions.ILike(c.Contact.Phone, term));
        }

        query = query.OrderByDescending(c => c.LastMessageAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ConversationResponse(
                c.Id, c.ContactId, c.Contact.Name, c.Contact.Phone,
                c.WabaConnectionId, c.WabaConnection.DisplayPhoneNumber,
                c.Status, c.LastMessageAt, c.LastMessageBody, c.LastMessageDirection,
                c.UnreadCount, c.WindowExpiresAt, c.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ConversationResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ConversationResponse(
                c.Id, c.ContactId, c.Contact.Name, c.Contact.Phone,
                c.WabaConnectionId, c.WabaConnection.DisplayPhoneNumber,
                c.Status, c.LastMessageAt, c.LastMessageBody, c.LastMessageDirection,
                c.UnreadCount, c.WindowExpiresAt, c.CreatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Conversation", id);

        return conversation;
    }

    public async Task<(IReadOnlyList<ConversationMessageResponse> Items, int Total)> GetMessagesAsync(
        Guid conversationId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Existence check under the tenant filter → a clean 404 for an unknown / foreign conversation.
        var exists = await db.Conversations.AnyAsync(c => c.Id == conversationId, ct);
        if (!exists) throw new NotFoundException("Conversation", conversationId);

        // Exclude messages an agent deleted from the inbox ("delete for me").
        var query = db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted);

        var total = await query.CountAsync(ct);
        // Newest-first so page 1 is the latest block; the client reverses each page for display
        // (newest at the bottom) and pages backwards to load older history.
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ConversationMessageResponse(
                m.Id, m.ConversationId, m.Body, m.Direction, m.Status,
                m.ExternalMessageId, m.ErrorMessage, m.CreatedAt, m.StatusAt,
                m.MediaType, m.MediaMimeType, m.MediaDownloadedAt != null))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ConversationMessageResponse> SendMessageAsync(
        Guid conversationId, string body, CancellationToken ct = default)
    {
        var conversation = await db.Conversations
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        if (!conversation.WabaConnection.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The WhatsApp connection for this conversation is inactive.");

        // Opted-out contacts: warn only — the UI surfaces IsOptedOut from ContactResponse so
        // agents can proceed deliberately (e.g. support replies after a marketing STOP).
        // Only campaign / business-initiated sends are hard-blocked.

        // 24-hour customer-service window pre-check. Meta's 131047 is the authoritative backstop, but
        // pre-checking gives a clean error + a disabled composer instead of a wasted API round-trip.
        if (conversation.WindowExpiresAt is null || conversation.WindowExpiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("WINDOW_CLOSED",
                "The 24-hour customer service window has closed. You can reply again after the customer "
                + "messages you (or by sending an approved template).");

        var message = await SendInThreadAsync(
            conversation, conversation.Contact, conversation.WabaConnection, body, ct);

        return MapMessage(message);
    }

    public async Task MarkReadAsync(Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        if (conversation.UnreadCount == 0) return;

        conversation.UnreadCount = 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteMessageForMeAsync(Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        // Tenant filter scopes both loads to the caller's company → a foreign id is a clean 404.
        var conversation = await db.Conversations
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        var message = await db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct)
            ?? throw new NotFoundException("Message", messageId);

        if (message.IsDeleted) return; // Idempotent: already deleted → nothing to do.

        message.IsDeleted = true;
        message.DeletedAt = DateTime.UtcNow;

        // If the deleted message was the conversation's latest, refresh the inbox preview from the newest
        // remaining (non-deleted) message so the list doesn't keep showing the removed text.
        var latest = await db.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted && m.Id != messageId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.CreatedAt, m.Body, m.Direction })
            .FirstOrDefaultAsync(ct);

        if (latest is not null)
        {
            conversation.LastMessageAt = latest.CreatedAt;
            conversation.LastMessageBody = Truncate(latest.Body);
            conversation.LastMessageDirection = latest.Direction;
        }
        else
        {
            // The thread's last visible message was removed — clear the preview (keep LastMessageAt as the
            // sort anchor so the empty thread doesn't jump around the inbox).
            conversation.LastMessageBody = null;
        }

        await db.SaveChangesAsync(ct);

        await notifier.MessageDeletedAsync(
            conversation.CompanyId, conversationId, messageId,
            MapConversation(conversation, conversation.Contact, conversation.WabaConnection), ct);
    }

    public async Task<MessageResponse> SendToContactAsync(
        Guid contactId, Guid wabaConnectionId, string body, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, ct)
            ?? throw new NotFoundException("Contact", contactId);

        var waba = await db.WabaConnections.FirstOrDefaultAsync(w => w.Id == wabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", wabaConnectionId);

        if (!waba.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The selected WhatsApp connection is inactive.");

        var conversation = await FindOrCreateOpenAsync(contact, waba, ct);

        // 24-hour customer-service window pre-check (M6) — mirror the two primary send paths. Meta's 131047 is
        // the authoritative backstop, but pre-checking avoids a quality-damaging rejection round-trip on a
        // cold / expired-window contact and returns a clean error instead.
        if (conversation.WindowExpiresAt is null || conversation.WindowExpiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("WINDOW_CLOSED",
                "The 24-hour customer service window has closed. You can reply again after the customer "
                + "messages you (or by sending an approved template).");

        var message = await SendInThreadAsync(conversation, contact, waba, body, ct);

        return new MessageResponse(
            message.Id, contact.Id, contact.Name, contact.Phone,
            waba.Id, waba.DisplayPhoneNumber, message.Body, message.Direction,
            message.Status, message.ExternalMessageId, message.ErrorMessage, message.CreatedAt);
    }

    public async Task<ConversationResponse> StartConversationAsync(
        Guid contactId, Guid wabaConnectionId, string body, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, ct)
            ?? throw new NotFoundException("Contact", contactId);

        var waba = await db.WabaConnections.FirstOrDefaultAsync(w => w.Id == wabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", wabaConnectionId);

        if (!waba.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The selected WhatsApp connection is inactive.");

        var conversation = await FindOrCreateOpenAsync(contact, waba, ct);

        // Don't throw on a Meta rejection (e.g. a closed 24h window for a cold contact): the thread should
        // still open so the agent sees their failed message + the window state. The Message row records the
        // failure; the snapshot + realtime push happen inside SendInThreadAsync regardless.
        await SendInThreadAsync(conversation, contact, waba, body, ct, throwOnFailure: false);

        return MapConversation(conversation, contact, waba);
    }

    /// <summary>
    /// Shared outbound tail: send via the core, update the snapshot (outbound never resets the window or
    /// the unread count), push the realtime event, then throw if Meta rejected — so the thread shows the
    /// failed attempt and the caller still gets a 422.
    /// </summary>
    private async Task<Message> SendInThreadAsync(
        Conversation conversation, Contact contact, WabaConnection waba, string body, CancellationToken ct,
        bool throwOnFailure = true)
    {
        var message = await sender.SendAsync(contact, waba, body, conversation.Id, ct);

        conversation.LastMessageAt = message.CreatedAt;
        conversation.LastMessageBody = Truncate(body);
        conversation.LastMessageDirection = MessageDirection.Outbound;
        await db.SaveChangesAsync(ct);

        await notifier.MessageAsync(
            conversation.CompanyId, MapConversation(conversation, contact, waba), MapMessage(message), ct);

        if (throwOnFailure && message.Status == MessageStatus.Failed)
            throw new BusinessRuleException("WHATSAPP_SEND_FAILED",
                message.ErrorMessage ?? "Failed to send message via WhatsApp.");

        return message;
    }

    /// <summary>
    /// Returns the contact's open conversation, creating one when none exists. Tolerates the partial-unique
    /// race (a concurrent create) by detaching + re-querying — mirrors WebhookInboxService.PersistAsync.
    /// </summary>
    private async Task<Conversation> FindOrCreateOpenAsync(Contact contact, WabaConnection waba, CancellationToken ct)
    {
        var existing = await db.Conversations.FirstOrDefaultAsync(
            c => c.ContactId == contact.Id && c.WabaConnectionId == waba.Id && c.Status == ConversationStatus.Open, ct);
        if (existing is not null) return existing;

        var conversation = new Conversation
        {
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            Status = ConversationStatus.Open,
            LastMessageAt = DateTime.UtcNow,
            // CompanyId auto-stamped by AuditInterceptor (HTTP path).
        };
        db.Conversations.Add(conversation);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(conversation).State = EntityState.Detached;
            existing = await db.Conversations.FirstOrDefaultAsync(
                c => c.ContactId == contact.Id && c.WabaConnectionId == waba.Id && c.Status == ConversationStatus.Open, ct);
            if (existing is not null) return existing;
            throw;
        }

        return conversation;
    }

    // ── Mapping ────────────────────────────────────────────────────────────
    // In-memory map for the realtime push (entities already loaded, so no SQL translation needed).
    private static ConversationResponse MapConversation(Conversation c, Contact contact, WabaConnection waba) => new(
        c.Id, c.ContactId, contact.Name, contact.Phone,
        c.WabaConnectionId, waba.DisplayPhoneNumber,
        c.Status, c.LastMessageAt, c.LastMessageBody, c.LastMessageDirection,
        c.UnreadCount, c.WindowExpiresAt, c.CreatedAt);

    private static ConversationMessageResponse MapMessage(Message m) => new(
        m.Id, m.ConversationId, m.Body, m.Direction, m.Status,
        m.ExternalMessageId, m.ErrorMessage, m.CreatedAt, m.StatusAt);

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];
}
