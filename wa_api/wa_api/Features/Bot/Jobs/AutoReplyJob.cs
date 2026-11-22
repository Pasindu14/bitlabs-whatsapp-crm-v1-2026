using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Jobs;

/// <summary>
/// Answers one inbound message with the first matching auto-reply rule.
/// <para>
/// Runs OUT of the webhook batch transaction (enqueued by <c>InboundMessageWebhookHandler</c>), for the
/// same reason as <c>InboundMediaDownloadJob</c>: sending calls Meta over HTTP, and doing that inside the
/// webhook transaction would pin a pooled DB connection across network I/O against the small Supavisor
/// cap. Like every background job here it has NO tenant context, so it reads with
/// <c>IgnoreQueryFilters()</c> and stamps <c>CompanyId</c> explicitly.
/// </para>
/// </summary>
public sealed class AutoReplyJob(
    AppDbContext db,
    IAutoReplyEngine engine,
    IWhatsAppMessageSender sender,
    IChatNotifier notifier,
    ILogger<AutoReplyJob> logger)
{
    /// <summary>
    /// Minimum gap between two auto-replies in the SAME conversation. Stops a customer sending ten
    /// messages in a row from triggering ten outbound sends — which reads as spam to Meta and burns
    /// the number's quality rating.
    /// </summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private const int PreviewLength = 200;

    // "default" queue, not "webhooks": a slow Meta send must never starve webhook-processing latency.
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 60, 300 })]
    public async Task RunAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId, ct)
            // Not yet committed (racing the webhook transaction) or rolled back. Throw → Hangfire retries;
            // by the first retry it has committed, or it never existed and this dead-letters harmlessly.
            ?? throw new InvalidOperationException($"Message {messageId} not found yet for auto-reply.");

        // ── Guard 1: inbound only. An outbound row must never provoke a reply. ──
        if (message.Direction != MessageDirection.Inbound)
            return;

        // ── Guard 2: loop breaker. A bot reply is itself a Message; if one could ever be re-fed to
        // this job the two sides would ping-pong forever. Inbound rows are never flagged, so this is
        // belt-and-braces against a future call site. ──
        if (message.IsAutoReply)
            return;

        if (message.ConversationId is not { } conversationId)
            return;

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
            return;

        // ── Guard 3: opt-out is a hard stop. The inbound handler sets IsOptedOut in the same batch
        // when the customer sends STOP, so by the time this runs the flag is authoritative. Replying
        // to a STOP is a Meta policy violation, not merely impolite. ──
        if (conversation.Contact.IsOptedOut)
        {
            logger.LogInformation(
                "Auto-reply skipped for message {MessageId} — contact has opted out.", messageId);
            return;
        }

        if (!conversation.WabaConnection.IsActive)
            return;

        // ── Guard 4: 24-hour customer-service window. An inbound message resets it, so this is
        // normally open — but the job runs after commit and may be retried minutes later, so it is
        // re-checked rather than assumed. Free-form text outside the window is rejected by Meta. ──
        if (conversation.WindowExpiresAt is null || conversation.WindowExpiresAt <= DateTime.UtcNow)
        {
            logger.LogInformation(
                "Auto-reply skipped for message {MessageId} — 24h window closed.", messageId);
            return;
        }

        // ── Guard 5: per-conversation cooldown. ──
        var cooldownStart = DateTime.UtcNow - Cooldown;
        var repliedRecently = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                        && m.IsAutoReply
                        && m.CreatedAt >= cooldownStart, ct);
        if (repliedRecently)
        {
            logger.LogInformation(
                "Auto-reply skipped for message {MessageId} — another reply sent within the last {Seconds}s.",
                messageId, Cooldown.TotalSeconds);
            return;
        }

        // Idempotency + deference to humans: if ANY outbound message already exists at or after this
        // inbound one, the thread has been answered and this job has nothing to add.
        //
        // Deliberately keyed on Direction, NOT IsAutoReply. The sender persists and commits the reply
        // row BEFORE this job can stamp IsAutoReply on it, so an IsAutoReply-only check would miss a
        // reply that was already sent when a crash landed between those two saves — and re-send it on
        // the Hangfire retry. Checking direction closes that window, and gives a second property for
        // free: when a human agent has already replied, the bot stays out of the way.
        var alreadyAnswered = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                        && m.Direction == MessageDirection.Outbound
                        && m.CreatedAt >= message.CreatedAt, ct);
        if (alreadyAnswered)
            return;

        var rules = await db.AutoReplyRules.IgnoreQueryFilters()
            .Where(r => r.CompanyId == message.CompanyId && r.IsActive && r.IsEnabled)
            .ToListAsync(ct);
        if (rules.Count == 0)
            return;

        // A Welcome rule fires only on the customer's opening message: no earlier inbound in the thread.
        var isFirstInbound = !await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                        && m.Direction == MessageDirection.Inbound
                        && m.CreatedAt < message.CreatedAt, ct);

        var rule = engine.Match(rules, message.Body, isFirstInbound);
        if (rule is null)
            return;

        // Billing mirrors ConversationService.SendInThreadAsync: a reply inside the open window is FREE,
        // because Meta bills the whole 24-hour window as one conversation. The window is open (guard 4),
        // so this never charges a credit.
        var reply = await sender.SendAsync(
            conversation.Contact, conversation.WabaConnection, rule.ReplyBody,
            conversation.Id, chargeCredit: false, ct);

        reply.IsAutoReply = true;

        // Thread snapshot, same as any outbound. WindowExpiresAt is deliberately NOT touched — only the
        // inbound webhook anchors it.
        conversation.LastMessageAt = reply.CreatedAt;
        conversation.LastMessageBody = Truncate(rule.ReplyBody);
        conversation.LastMessageDirection = MessageDirection.Outbound;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-replied to message {MessageId} with rule {RuleId} ({RuleName}) — status {Status}.",
            messageId, rule.Id, rule.Name, reply.Status);

        await NotifyAsync(conversation, reply, ct);
    }

    /// <summary>
    /// Best-effort realtime push so an agent watching the thread sees the bot's reply appear. A SignalR
    /// hiccup must never fail the job — the message is already sent and persisted.
    /// </summary>
    private async Task NotifyAsync(Conversation conversation, Message reply, CancellationToken ct)
    {
        try
        {
            var conversationDto = new ConversationResponse(
                conversation.Id, conversation.ContactId, conversation.Contact.Name, conversation.Contact.Phone,
                conversation.WabaConnectionId, conversation.WabaConnection.DisplayPhoneNumber, conversation.Status,
                conversation.LastMessageAt, conversation.LastMessageBody, conversation.LastMessageDirection,
                conversation.UnreadCount, conversation.WindowExpiresAt, conversation.CreatedAt, conversation.BotState, conversation.BotResumesAt,
                conversation.BotPausedByUserId != null);

            var messageDto = new ConversationMessageResponse(
                reply.Id, conversation.Id, reply.Body, reply.Direction, reply.Status,
                reply.ExternalMessageId, reply.ErrorMessage, reply.CreatedAt, reply.StatusAt,
                reply.MediaType, reply.MediaMimeType, MediaReady: false);

            await notifier.MessageAsync(conversation.CompanyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Auto-reply realtime push failed for conversation {ConversationId} — non-fatal.", conversation.Id);
        }
    }

    private static string Truncate(string s) => s.Length <= PreviewLength ? s : s[..PreviewLength];
}
