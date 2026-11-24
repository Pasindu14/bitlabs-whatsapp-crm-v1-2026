using System.Globalization;
using Hangfire;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.OptOut;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Jobs;
using wa_api.Features.Messages.InboundMedia;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Features.Webhooks.Processing;
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
    WebhookPostCommitActions postCommit,
    IBackgroundJobClient jobs,
    IMemoryCache cache,
    ILogger<InboundMessageWebhookHandler> logger,
    IBotGate? botGate = null)
    : IWebhookEventHandler
{
    private const int PreviewLength = 200;

    /// <summary>
    /// How old an inbound message may be and still get a bot reply. Meta retries a failed webhook with a
    /// growing backoff for days, so an outage delivers yesterday's "hi" this afternoon — and a bot answering
    /// it as if it had just arrived reads as the business messaging out of nowhere. Past this age the
    /// message is still stored for the inbox; it just doesn't wake the bot.
    /// </summary>
    public static readonly TimeSpan MaxBotReplyAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A number is saved in the clinic's CRM at most once in this long — measured from the last save, not
    /// the last message, so even a chat that never pauses re-saves a number the clinic deleted meanwhile.
    /// </summary>
    public static readonly TimeSpan ClinicSaveGap = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A number triggers at most one "new message" notification to the clinic's CRM in this long. A customer
    /// typing a question over three lines is one thing for the clinic to look at, not three.
    /// </summary>
    public static readonly TimeSpan ClinicNotifyGap = TimeSpan.FromMinutes(2);

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
        var savedToClinic = new HashSet<string>();

        // Asked once per batch (and cached by the gate): is the chatbot on for this company at all — the
        // platform switch, the plan, and something configured? For a company with no bot this handler then
        // enqueues nothing extra, so a campaign reply surge costs the same as it did before the chatbot
        // existed. A null gate (unit tests) keeps the "always hand over, the job decides" behaviour.
        var botLive = botGate is null || await botGate.IsLiveAsync(companyId, ct);
        var clinicConnected = botGate is null || await botGate.HasClinicCrmAsync(companyId, ct);
        // Opt-in (off by default), so a null gate means off — unlike the two checks above.
        var clinicWantsMessages = botGate is not null && clinicConnected
            && await botGate.NotifiesClinicCrmOfMessagesAsync(companyId, ct);

        // The team's own numbers receive the handoff alert, and a reply to it is a colleague talking, not a
        // customer — the bot must never answer one. Loaded once per batch; the list is a handful of numbers.
        List<string> teamPhones = botLive
            ? (await db.BotProfiles.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId, ct))?.HandoffAlertPhones ?? []
            : [];

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

            // The contact is saved in the clinic's CRM as an interested customer when they write, at most once
            // per ClinicSaveGap, so a number the clinic deleted there comes back within minutes — without a CRM
            // call for every line of a chat. After commit, like the jobs below; the job skips a company with no
            // CRM connection, and the CRM never duplicates a number. A restart forgets the timer, which only
            // means one extra (harmless) save.
            if (clinicConnected && !contact.IsOptedOut && !teamPhones.Contains(phone!) && savedToClinic.Add(phone!)
                && !cache.TryGetValue(ClinicSaveKey(companyId, phone!), out _))
            {
                var name = contact.Name == contact.Phone ? null : contact.Name;
                jobs.Enqueue<ClinicCrmLeadJob>(j => j.SaveAsync(companyId, phone!, name, CancellationToken.None));
                cache.Set(ClinicSaveKey(companyId, phone!), true, ClinicSaveGap);
            }

            var now = DateTime.UtcNow;

            // When the customer actually sent it, per Meta — not when it reached us. A retried delivery can
            // land hours late, and both the 24-hour window and the bot's decision to reply hang off this.
            var sentAt = SentAt(m.Timestamp, now);
            var isLateDelivery = now - sentAt > MaxBotReplyAge;

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
            // Meta opens the window from when the customer sent the message, so a late delivery must not
            // stretch it — and must never shorten a window a newer message already opened.
            var windowFromThis = sentAt.AddHours(24);
            if (conversation.WindowExpiresAt is null || conversation.WindowExpiresAt < windowFromThis)
                conversation.WindowExpiresAt = windowFromThis;
            // They wrote back, so this silence is over. Clearing it lets a later quiet patch earn its
            // own nudge, while a single silence can still only ever earn one.
            conversation.BotFollowUpSentAt = null;
            conversation.BotFollowUpStepsSent = 0;

            QueueNotify(companyId, conversation, contact, connection, message, now);

            // Tell the clinic's CRM a customer wrote — only for real content (not STOP, a reaction, a sticker
            // or a bare "ok"), never for someone who has opted out or for the team's own numbers, and at most
            // once per ClinicNotifyGap per number. After commit, like the other jobs; the job re-checks the
            // switch, so turning it off stops what is already queued.
            if (clinicWantsMessages && !contact.IsOptedOut && !teamPhones.Contains(phone!)
                && ClinicCrmMessageRelevance.IsMeaningful(m)
                && !cache.TryGetValue(ClinicNotifyKey(companyId, phone!), out _))
            {
                var name = contact.Name == contact.Phone ? null : contact.Name;
                jobs.Enqueue<ClinicCrmMessageJob>(j => j.NotifyAsync(companyId, phone!, name, CancellationToken.None));
                cache.Set(ClinicNotifyKey(companyId, phone!), true, ClinicNotifyGap);
            }

            // Fetch the media bytes AFTER the batch commits — never inside the webhook transaction, which would
            // pin a pooled DB connection across two slow Meta HTTP calls. Hangfire's enqueue is not part of the
            // EF transaction; the job tolerates the enqueue→commit race (retries) and a rolled-back batch
            // (the stale message id no-ops / dead-letters harmlessly).
            if (mediaType is not null && !string.IsNullOrWhiteSpace(mediaNode!.Id))
                jobs.Enqueue<InboundMediaDownloadJob>(j => j.RunAsync(message.Id, CancellationToken.None));

            // Hand the message to the chatbot AFTER the batch commits, for the same reason as the media
            // download above: the reply job calls Meta (and now Anthropic) over HTTP, which must never
            // happen inside the webhook transaction. The job re-checks opt-out, the 24h window, the bot
            // pause state and a per-conversation cooldown itself, so nothing is decided here — an
            // opted-out contact is still enqueued and the job declines. That keeps this handler's only
            // job "persist what arrived".
            //
            // No chatbot for this company (or the platform switch is off): nothing to hand over.
            if (!botLive) continue;

            // The one exception is a late delivery: the moment to answer it has passed, and a reply now
            // arrives with no visible prompt on the customer's side. A human can still pick it up.
            if (isLateDelivery)
            {
                logger.LogInformation(
                    "Inbound wamid {Wamid} was sent at {SentAt:o} but arrived {Delay} late — stored, no bot reply.",
                    wamid, sentAt, now - sentAt);
                continue;
            }

            if (teamPhones.Contains(phone!))
            {
                logger.LogInformation("Inbound message from team number {Phone} — stored, no bot reply.", phone);
                continue;
            }

            jobs.Enqueue<AiReplyJob>(j => j.RunAsync(message.Id, CancellationToken.None));
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

    private static string ClinicSaveKey(Guid companyId, string phone) => $"clinic-crm-lead:{companyId}:{phone}";

    private static string ClinicNotifyKey(Guid companyId, string phone) => $"clinic-crm-message:{companyId}:{phone}";

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

    private void QueueNotify(
        Guid companyId, Conversation conversation, Contact contact, WabaConnection connection,
        Message message, DateTime now)
    {
        // Realtime hint, sent only AFTER the batch commits: pushed from here, the inbox refetched on the event,
        // read the not-yet-committed state and kept it until the agent switched chats. The DTOs are built now,
        // from this attempt's tracked entities; a rolled-back attempt discards the queue. Best-effort — the
        // runner swallows a SignalR failure. Timestamps aren't stamped until commit, so we use `now`.
        var conversationDto = new ConversationResponse(
            conversation.Id, contact.Id, contact.Name, contact.Phone,
            connection.Id, connection.DisplayPhoneNumber, conversation.Status,
            conversation.LastMessageAt, conversation.LastMessageBody, conversation.LastMessageDirection,
            conversation.UnreadCount, conversation.WindowExpiresAt,
            conversation.CreatedAt == default ? now : conversation.CreatedAt,
            conversation.BotState, conversation.BotResumesAt, conversation.BotPausedByUserId != null);

        var messageDto = new ConversationMessageResponse(
            message.Id, conversation.Id, message.Body, message.Direction, message.Status,
            message.ExternalMessageId, message.ErrorMessage, now, null,
            // Media not yet downloaded → MediaReady:false. The download job re-pushes with it ready.
            message.MediaType, message.MediaMimeType, MediaReady: false);

        postCommit.Add(ct => notifier.MessageAsync(companyId, conversationDto, messageDto, ct));
    }

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];

    /// <summary>
    /// Meta's send time (epoch seconds as a string). A missing or unparseable value counts as "just now" so
    /// a malformed payload degrades to the old behaviour rather than silencing the bot; a timestamp ahead of
    /// our clock (skew) is clamped to now so it can't push the window past 24 hours.
    /// </summary>
    private static DateTime SentAt(string? timestamp, DateTime now)
    {
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            return now;

        var sent = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return sent > now ? now : sent;
    }

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
