using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.OptOut;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Messages.InboundMedia;
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
    IBackgroundJobClient jobs,
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

            // Media messages (image/video/audio/document/sticker) carry an opaque media id, not text. Detect
            // which node is present; its bytes are fetched asynchronously after commit (InboundMediaDownloadJob).
            var (mediaType, mediaNode) = ResolveMedia(m);

            // Prefer the human-readable text of whatever the customer sent — a media caption, free text, a
            // tapped template quick-reply ("button"), or an interactive reply — falling back to the type tag.
            var body = FirstNonBlank(
                mediaNode?.Caption,
                m.Text?.Body,
                m.Button?.Text,
                m.Interactive?.ButtonReply?.Title,
                m.Interactive?.ListReply?.Title) ?? $"[{m.Type ?? "message"}]";
            if (body.Length > 4096) body = body[..4096];

            // Opt-out detection: a Stop can arrive as free text, a tapped template quick-reply button
            // (type:"button", which carries NO text.body), or an interactive reply — check every channel.
            // Suppression is a hard stop; only a company admin can lift it from the contact screen.
            if (!contact.IsOptedOut && HasStopIntent(m))
            {
                contact.IsOptedOut = true;
                contact.OptedOutAt = now;
                contact.HasOptedIn = false;
                contact.ConsentSource = ConsentSource.None;
                logger.LogInformation(
                    "Contact {Phone} opted out via {Type} — suppressed from future outbound sends.",
                    phone, m.Type ?? "message");
            }

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
                // Media metadata; the bytes are downloaded post-commit (null MetaMediaId ⇒ plain text, no job).
                MediaType = mediaType,
                MediaMimeType = mediaNode?.MimeType,
                MediaFileName = mediaNode?.Filename,
                MetaMediaId = mediaNode?.Id,
            };
            db.Messages.Add(message);

            // Snapshot — inbound resets Meta's 24-hour customer-service window and bumps the unread count.
            conversation.LastMessageAt = now;
            conversation.LastMessageBody = Truncate(body);
            conversation.LastMessageDirection = MessageDirection.Inbound;
            conversation.UnreadCount += 1;
            conversation.WindowExpiresAt = now.AddHours(24);

            await NotifyAsync(companyId, conversation, contact, connection, message, now, ct);

            // Fetch the media bytes AFTER the batch commits — never inside the webhook transaction, which would
            // pin a pooled DB connection across two slow Meta HTTP calls. Hangfire's enqueue is not part of the
            // EF transaction; the job tolerates the enqueue→commit race (retries) and a rolled-back batch
            // (the stale message id no-ops / dead-letters harmlessly).
            if (mediaType is not null && !string.IsNullOrWhiteSpace(mediaNode!.Id))
                jobs.Enqueue<InboundMediaDownloadJob>(j => j.RunAsync(message.Id, CancellationToken.None));
        }

        // No SaveChanges — WebhookProcessingJob commits the batch once (atomic; discardable on retry).
    }

    /// <summary>
    /// Detects which media node an inbound message carries. WhatsApp puts the payload under a type-named
    /// property (<c>image</c>/<c>video</c>/…); we match on node presence so an unusual/missing <c>type</c>
    /// tag can't hide media. Returns <c>(null, null)</c> for a non-media (text/button/interactive) message.
    /// </summary>
    private static (string? Type, WebhookMedia? Node) ResolveMedia(WebhookInboundMessage m)
    {
        if (m.Image is not null) return ("image", m.Image);
        if (m.Video is not null) return ("video", m.Video);
        if (m.Audio is not null) return ("audio", m.Audio);
        if (m.Document is not null) return ("document", m.Document);
        if (m.Sticker is not null) return ("sticker", m.Sticker);
        return (null, null);
    }

    private async Task<Contact> UpsertContactAsync(Guid companyId, string phone, string? profileName, CancellationToken ct)
    {
        var contact = db.Contacts.Local.FirstOrDefault(c => c.CompanyId == companyId && c.Phone == phone)
            ?? await db.Contacts.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Phone == phone, ct);

        if (contact is null)
        {
            // A customer messaging us first IS the opt-in event under Meta's policy.
            contact = new Contact
            {
                CompanyId = companyId,
                Phone = phone,
                Name = string.IsNullOrWhiteSpace(profileName) ? phone : profileName!,
                HasOptedIn = true,
                OptedInAt = DateTime.UtcNow,
                ConsentSource = ConsentSource.InboundMessage,
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
                message.ExternalMessageId, message.ErrorMessage, now, null,
                // Media not yet downloaded → MediaReady:false. The download job re-pushes with it ready.
                message.MediaType, message.MediaMimeType, MediaReady: false);

            await notifier.MessageAsync(companyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push realtime inbound event for conversation {ConversationId} — non-fatal.", conversation.Id);
        }
    }

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];

    /// <summary>The first non-blank candidate, or null when all are blank.</summary>
    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>
    /// True when any channel of the inbound message carries an opt-out keyword. A tapped template
    /// quick-reply button arrives as type:"button" with <c>button.text</c>/<c>button.payload</c> and no
    /// <c>text.body</c>, and an interactive reply carries its label/id — so all of them are checked.
    /// </summary>
    private static bool HasStopIntent(WebhookInboundMessage m) =>
        OptOutKeywords.IsStopIntent(m.Text?.Body)
        || OptOutKeywords.IsStopIntent(m.Button?.Text)
        || OptOutKeywords.IsStopIntent(m.Button?.Payload)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ButtonReply?.Title)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ButtonReply?.Id)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ListReply?.Title)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ListReply?.Id);
}
