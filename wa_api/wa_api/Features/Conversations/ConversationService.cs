using System.Security.Claims;
using wa_api.Features.Templates.Entities;
using wa_api.Features.Templates;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Bot;
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
    IChatNotifier notifier,
    IHttpContextAccessor httpContextAccessor,
    IBotGate? botGate = null) : IConversationService
{
    /// <summary>Max preview length stored on the conversation snapshot.</summary>
    private const int PreviewLength = 200;

    /// <summary>
    /// How long the bot stays out of the way after an agent replies by hand. Long enough to finish a
    /// conversation, short enough that a thread is not silently orphaned when the agent moves on.
    /// </summary>
    private static readonly TimeSpan TakeoverDuration = TimeSpan.FromMinutes(60);

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
                c.UnreadCount, c.WindowExpiresAt, c.CreatedAt, c.BotState, c.BotResumesAt,
                c.BotPausedByUserId != null))
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
                c.UnreadCount, c.WindowExpiresAt, c.CreatedAt, c.BotState, c.BotResumesAt,
                c.BotPausedByUserId != null))
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

        // An agent typing IS the takeover. Pausing here rather than rejecting the send (the stricter
        // design in plan 005 D2) keeps the composer working exactly as it does today while closing the
        // real hazard: an AI running a multi-turn conversation talking over the person who just stepped
        // in. The bot comes back on its own after an hour, or the agent hands it back sooner.
        //
        // Only when there is a bot to pause: for a company without the chatbot this would stamp every
        // thread an agent replies on as "paused", then have the auto-resume job undo it an hour later.
        if (conversation.BotState == ConversationBotState.Active
            && (botGate is null || await botGate.IsLiveAsync(conversation.CompanyId, ct)))
        {
            conversation.BotState = ConversationBotState.Paused;
            conversation.BotPausedAt = DateTime.UtcNow;
            conversation.BotPausedByUserId = CurrentUserId();
            conversation.BotResumesAt = DateTime.UtcNow + TakeoverDuration;
        }

        var message = await SendInThreadAsync(
            conversation, conversation.Contact, conversation.WabaConnection, body, ct);

        return MapMessage(message);
    }

    public async Task<ConversationResponse> SetBotStateAsync(
        Guid conversationId, bool paused, int? resumeAfterMinutes, CancellationToken ct = default)
    {
        var conversation = await db.Conversations
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        if (paused)
        {
            conversation.BotState = ConversationBotState.Paused;
            conversation.BotPausedAt = DateTime.UtcNow;
            conversation.BotPausedByUserId = CurrentUserId();
            // No duration means the agent owns this thread until they say otherwise.
            conversation.BotResumesAt = resumeAfterMinutes is > 0
                ? DateTime.UtcNow.AddMinutes(resumeAfterMinutes.Value)
                : null;
        }
        else
        {
            conversation.BotState = ConversationBotState.Active;
            conversation.BotPausedAt = null;
            conversation.BotPausedByUserId = null;
            conversation.BotResumesAt = null;
        }

        await db.SaveChangesAsync(ct);

        var response = MapConversation(conversation, conversation.Contact, conversation.WabaConnection);

        // Push it so a second agent's open inbox sees the handover rather than typing over the first.
        try
        {
            await notifier.BotStateChangedAsync(conversation.CompanyId, response, ct);
        }
        catch (Exception)
        {
            // Realtime is a convenience; the state is already committed.
        }

        return response;
    }

    /// <summary>
    /// The acting agent, for the audit trail on a takeover. Null outside a request (and harmless there —
    /// the bot pausing itself has no user to record).
    /// </summary>
    private Guid? CurrentUserId()
    {
        var raw = httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
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
        Guid contactId, Guid wabaConnectionId, string? body,
        Guid? templateId = null, IReadOnlyList<string>? templateVariables = null, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, ct)
            ?? throw new NotFoundException("Contact", contactId);

        var waba = await db.WabaConnections.FirstOrDefaultAsync(w => w.Id == wabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", wabaConnectionId);

        if (!waba.IsActive)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The selected WhatsApp connection is inactive.");

        if (templateId is null && string.IsNullOrWhiteSpace(body))
            throw new BusinessRuleException("MESSAGE_REQUIRED", "Write a message or choose a template to send.");

        // Free text only reaches a customer inside their 24-hour window; outside it Meta refuses it (131047)
        // after we have already opened a thread and spent a send. Refuse it here instead, with the fix.
        if (templateId is null)
        {
            var existing = await db.Conversations.FirstOrDefaultAsync(
                c => c.ContactId == contact.Id && c.WabaConnectionId == waba.Id && c.Status == ConversationStatus.Open, ct);
            if (existing?.WindowExpiresAt is not { } until || until <= DateTime.UtcNow)
                throw new BusinessRuleException("WINDOW_CLOSED",
                    $"{(string.IsNullOrWhiteSpace(contact.Name) ? "This customer" : contact.Name)} hasn't messaged "
                    + "this number in the last 24 hours, so WhatsApp only allows an approved template. "
                    + "Choose a template to start the chat.");
        }

        // Validate the template before a thread is opened, so a bad pick leaves nothing behind.
        Template? template = null;
        TemplateFillResult? fill = null;
        if (templateId is { } tid)
        {
            template = await db.Templates.FirstOrDefaultAsync(t => t.Id == tid, ct)
                ?? throw new NotFoundException("Template", tid);

            if (template.Status != TemplateStatus.Approved)
                throw new BusinessRuleException("TEMPLATE_NOT_APPROVED",
                    "Only templates Meta has approved can be sent. This one is " + template.Status.ToString().ToLowerInvariant() + ".");

            if (template.WabaConnectionId != waba.Id)
                throw new BusinessRuleException("TEMPLATE_WRONG_NUMBER",
                    $"This template belongs to a different WhatsApp number. Choose one made for {waba.DisplayPhoneNumber}.");

            if (!TemplateSendability.IsBodyOnly(template))
                throw new BusinessRuleException("TEMPLATE_NEEDS_CAMPAIGN",
                    "This template has an image, a header value or a button link to fill in. Send it from Campaigns instead.");

            var values = templateVariables ?? [];
            fill = TemplateFill.Build(template, values.ElementAtOrDefault(0), values.Skip(1).ToList());
            if (!fill.CanSend)
                throw new BusinessRuleException("TEMPLATE_VALUES_INVALID",
                    $"Fill in every value in the template ({fill.Problem}). Each value must be under {TemplateFill.MaxParameterLength} characters.");
        }

        var conversation = await FindOrCreateOpenAsync(contact, waba, ct);

        if (template is not null && fill is not null)
        {
            // Same rule as the reply path: a rejection is recorded on the message and shown in the thread,
            // not thrown, so the agent sees what happened where they are looking.
            var sent = await sender.SendTemplateAsync(
                contact, waba, template.Name, template.Language, fill.Parameters, fill.RenderedBody, conversation.Id, ct);
            await RecordOutboundAsync(conversation, contact, waba, sent, fill.RenderedBody, ct);
        }
        else
        {
            await SendInThreadAsync(conversation, contact, waba, body!, ct, throwOnFailure: false);
        }

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
        // Billing: a reply inside the open 24-hour customer-service window is FREE. Meta bills that whole
        // window as one conversation, not per message, so only the business-initiated send that opens a
        // cold thread (WindowExpiresAt still null) costs a credit. Read-only — the outbound path must never
        // write WindowExpiresAt, or the SaveChanges below would clobber the inbound webhook's anchor.
        var chargeCredit = conversation.WindowExpiresAt is null || conversation.WindowExpiresAt <= DateTime.UtcNow;

        var message = await sender.SendAsync(contact, waba, body, conversation.Id, chargeCredit, ct);

        await RecordOutboundAsync(conversation, contact, waba, message, body, ct);

        if (throwOnFailure && message.Status == MessageStatus.Failed)
            throw new BusinessRuleException("WHATSAPP_SEND_FAILED",
                message.ErrorMessage ?? "Failed to send message via WhatsApp.");

        return message;
    }

    /// <summary>
    /// After any outbound send: refresh the thread snapshot (outbound never touches the window or the unread
    /// count) and push the realtime event so every open inbox shows it.
    /// </summary>
    private async Task RecordOutboundAsync(
        Conversation conversation, Contact contact, WabaConnection waba, Message message, string preview,
        CancellationToken ct)
    {
        conversation.LastMessageAt = message.CreatedAt;
        conversation.LastMessageBody = Truncate(preview);
        conversation.LastMessageDirection = MessageDirection.Outbound;
        await db.SaveChangesAsync(ct);

        await notifier.MessageAsync(
            conversation.CompanyId, MapConversation(conversation, contact, waba), MapMessage(message), ct);
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
        c.UnreadCount, c.WindowExpiresAt, c.CreatedAt, c.BotState, c.BotResumesAt,
        c.BotPausedByUserId != null);

    private static ConversationMessageResponse MapMessage(Message m) => new(
        m.Id, m.ConversationId, m.Body, m.Direction, m.Status,
        m.ExternalMessageId, m.ErrorMessage, m.CreatedAt, m.StatusAt);

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];
}
