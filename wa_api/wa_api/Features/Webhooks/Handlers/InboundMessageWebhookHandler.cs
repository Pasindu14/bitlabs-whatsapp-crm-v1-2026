using Microsoft.EntityFrameworkCore;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Handlers;

/// <summary>
/// PRD 7.3 — inbound customer messages. Upserts the contact, finds/opens the conversation, writes the
/// inbound <see cref="Message"/>, refreshes the thread snapshot (preview + unread + 24h window), and pushes
/// a realtime event.
/// <para>
/// This runs in the webhook background job, which has NO tenant context, so it does the opposite of the
/// HTTP path: it stamps <c>CompanyId</c> EXPLICITLY (the AuditInterceptor can't), reads with
/// <c>IgnoreQueryFilters()</c> (the global filter would hide every row), and never calls
/// <c>SaveChanges</c> — <c>WebhookProcessingJob</c> commits the whole batch once. The entity graph is wired
/// via navigation properties so EF orders the inserts (contact → conversation → message) on that one commit,
/// and intra-batch duplicates are caught via <c>ChangeTracker.Local</c> (uncommitted rows are invisible to a
/// fresh DB query).
/// </para>
/// </summary>
public sealed class InboundMessageWebhookHandler(
    AppDbContext db,
    IChatNotifier notifier,
    ILogger<InboundMessageWebhookHandler> logger)
    : IWebhookEventHandler
{
    private const int PreviewLength = 200;

    public bool CanHandle(string field, WebhookChange change)
        => field == "messages" && change.Value?.Messages is { Count: > 0 };

    public async Task HandleAsync(WebhookContext ctx, CancellationToken ct = default)
    {
        var connection = ctx.Connection;
        if (connection is null)
        {
            logger.LogWarning(
                "Inbound messages for an unrecognized phone_number_id — skipped (no company to attribute them to).");
            return;
        }

        var companyId = connection.CompanyId;
        var wabaId = connection.Id;
        var value = ctx.Change.Value!;
        var profiles = value.Contacts;

        foreach (var m in value.Messages!)
        {
            var phone = m.From;
            if (string.IsNullOrWhiteSpace(phone))
                continue;

            // Idempotency: drop a wamid we've already stored. Local covers this batch; the DB query covers a
            // prior delivery. (The webhook inbox also dedupes byte-identical replays upstream.)
            var wamid = m.Id;
            if (!string.IsNullOrWhiteSpace(wamid) &&
                (db.Messages.Local.Any(x => x.ExternalMessageId == wamid) ||
                 await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.ExternalMessageId == wamid, ct)))
            {
                logger.LogInformation("Inbound wamid {Wamid} already stored — skipped (duplicate delivery).", wamid);
                continue;
            }

            var profileName = profiles?.FirstOrDefault(c => c.WaId == phone)?.Profile?.Name;
            var contact = await UpsertContactAsync(companyId, phone!, profileName, ct);
            var conversation = await FindOrCreateOpenAsync(companyId, wabaId, contact, ct);

            var now = DateTime.UtcNow;
            var body = !string.IsNullOrWhiteSpace(m.Text?.Body) ? m.Text!.Body! : $"[{m.Type ?? "message"}]";
            if (body.Length > 4096) body = body[..4096];

            var message = new Message
            {
                CompanyId = companyId,            // stamped explicitly — no tenant context in the job
                Contact = contact,                // nav → correct insert order when the contact is new
                WabaConnectionId = wabaId,        // existing row → scalar FK is fine
                Conversation = conversation,      // nav → correct insert order + sets ConversationId
                Body = body,
                Direction = MessageDirection.Inbound,
                Status = MessageStatus.Delivered, // inbound has no delivery lifecycle; the UI ignores it
                ExternalMessageId = wamid,
            };
            db.Messages.Add(message);

            // Snapshot — inbound resets Meta's 24-hour customer-service window and bumps the unread count.
            conversation.LastMessageAt = now;
            conversation.LastMessageBody = Truncate(body);
            conversation.LastMessageDirection = MessageDirection.Inbound;
            conversation.UnreadCount += 1;
            conversation.WindowExpiresAt = now.AddHours(24);

            await NotifyAsync(companyId, conversation, contact, connection, message, now, ct);
        }

        // No SaveChanges — WebhookProcessingJob commits the batch once (atomic; discardable on retry).
    }

    private async Task<Contact> UpsertContactAsync(Guid companyId, string phone, string? profileName, CancellationToken ct)
    {
        var contact = db.Contacts.Local.FirstOrDefault(c => c.CompanyId == companyId && c.Phone == phone)
            ?? await db.Contacts.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Phone == phone, ct);

        if (contact is null)
        {
            contact = new Contact
            {
                CompanyId = companyId,
                Phone = phone,
                Name = string.IsNullOrWhiteSpace(profileName) ? phone : profileName!,
            };
            db.Contacts.Add(contact);
        }
        else if (!string.IsNullOrWhiteSpace(profileName) &&
                 (string.IsNullOrWhiteSpace(contact.Name) || contact.Name == contact.Phone))
        {
            // Fill in the real display name the first time Meta gives it to us.
            contact.Name = profileName!;
        }

        return contact;
    }

    private async Task<Conversation> FindOrCreateOpenAsync(Guid companyId, Guid wabaId, Contact contact, CancellationToken ct)
    {
        var conversation = db.Conversations.Local.FirstOrDefault(c =>
                c.CompanyId == companyId && c.ContactId == contact.Id
                && c.WabaConnectionId == wabaId && c.Status == ConversationStatus.Open)
            ?? await db.Conversations.IgnoreQueryFilters().FirstOrDefaultAsync(c =>
                c.CompanyId == companyId && c.ContactId == contact.Id
                && c.WabaConnectionId == wabaId && c.Status == ConversationStatus.Open, ct);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                CompanyId = companyId,
                Contact = contact,           // nav → contact inserts before the conversation
                WabaConnectionId = wabaId,
                Status = ConversationStatus.Open,
                LastMessageAt = DateTime.UtcNow,
            };
            db.Conversations.Add(conversation);
        }

        return conversation;
    }

    private async Task NotifyAsync(
        Guid companyId, Conversation conversation, Contact contact, WabaConnection connection,
        Message message, DateTime now, CancellationToken ct)
    {
        // Best-effort realtime hint, pushed BEFORE the batch commit (the pipeline commits once and exposes
        // no post-commit hook). On the rare rollback + Hangfire retry the event simply re-fires; the client
        // keys messages by id and reconciles on its next fetch. A SignalR hiccup must never fail webhook
        // processing, so this is swallowed. Timestamps aren't stamped until commit, so we use `now`.
        try
        {
            var conversationDto = new ConversationResponse(
                conversation.Id, contact.Id, contact.Name, contact.Phone,
                connection.Id, connection.DisplayPhoneNumber, conversation.Status,
                conversation.LastMessageAt, conversation.LastMessageBody, conversation.LastMessageDirection,
                conversation.UnreadCount, conversation.WindowExpiresAt,
                conversation.CreatedAt == default ? now : conversation.CreatedAt);

            var messageDto = new ConversationMessageResponse(
                message.Id, conversation.Id, message.Body, message.Direction, message.Status,
                message.ExternalMessageId, message.ErrorMessage, now, null);

            await notifier.MessageAsync(companyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push realtime inbound event for conversation {ConversationId} — non-fatal.", conversation.Id);
        }
    }

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];
}
