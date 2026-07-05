using System.Globalization;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Handlers;

/// <summary>
/// PRD 6.4 — applies outbound delivery statuses (sent/delivered/read/failed) to the owning Message.
/// Forward-only via <see cref="Rank"/>: a lower- or equal-rank status (out-of-order or duplicate callback)
/// never regresses the row, and a Failed message is never resurrected. Captures error code + pricing.
/// </summary>
public sealed class MessageStatusWebhookHandler(AppDbContext db, ILogger<MessageStatusWebhookHandler> logger)
    : IWebhookEventHandler
{
    // Accepted < Sent < Delivered < Read (PRD §5: forward-only StatusRank). Failed is terminal, handled
    // separately. Accepted (rank 0) is the initial state for outbound sends — a wamid is back but Meta's
    // 'sent' webhook hasn't landed — so an incoming 'sent' (rank 1) promotes Accepted → Sent.
    private static readonly Dictionary<MessageStatus, int> Rank = new()
    {
        [MessageStatus.Accepted] = 0,
        [MessageStatus.Sent] = 1,
        [MessageStatus.Delivered] = 2,
        [MessageStatus.Read] = 3,
    };

    public bool CanHandle(string field, WebhookChange change)
        => field == "messages" && change.Value?.Statuses is { Count: > 0 };

    public async Task HandleAsync(WebhookContext ctx, CancellationToken ct = default)
    {
        // Collect (messageId → new status) for campaign messages so we can sync recipients in one pass.
        var campaignMessageUpdates = new Dictionary<Guid, (MessageStatus Status, string? ErrorCode)>();

        // Accumulate quota to credit back, keyed by the EXACT subscription that was charged
        // (Message.MeteredSubscriptionId). A message metered on accept that now fails un-billed must be
        // refunded, but only against the row it drew down — not "the current active sub", which may be a
        // different row after a renewal/re-subscribe. Applied once, atomically, at the end.
        var refundsBySubscription = new Dictionary<Guid, int>();

        foreach (var s in ctx.Change.Value!.Statuses!)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Status))
                continue;

            // Delivery statuses are only ever about OUTBOUND messages. Filtering on Direction guards against
            // a (globally unlikely, but latent) wamid collision matching an inbound row — which, being stored
            // as Delivered and never metered, would otherwise phantom-refund quota on a 'failed' status (L1).
            var message = await db.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ExternalMessageId == s.Id
                                       && m.Direction == MessageDirection.Outbound, ct);
            if (message is null)
            {
                logger.LogInformation("Delivery status '{Status}' for unknown wamid {Wamid} — ignored.", s.Status, s.Id);
                continue;
            }

            // Pricing is additive metadata — capture it whenever Meta includes it.
            if (s.Pricing is { } p)
            {
                if (p.Billable is { } billable) message.Billable = billable;
                if (!string.IsNullOrWhiteSpace(p.Category)) message.Category = p.Category;
            }

            var statusAt = ParseTimestamp(s.Timestamp);

            if (string.Equals(s.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                // Idempotent: Meta re-delivers callbacks. If this message is already Failed, do nothing —
                // don't re-stamp and (crucially) don't refund a second time.
                if (message.Status == MessageStatus.Failed)
                    continue;

                // Forward-only for the failed branch too: a message Meta already confirmed Delivered/Read
                // cannot genuinely "fail". A late, out-of-order 'failed' callback for such a message is noise
                // — don't regress its status, and DON'T refund it (a delivered message was billed). Only a
                // message still at Accepted or Sent (queued/accepted, never confirmed delivered) can be
                // failed here — e.g. a 131049 non-delivery that arrives before any 'sent' webhook.
                if (message.Status is MessageStatus.Delivered or MessageStatus.Read)
                    continue;

                var err = s.Errors?.FirstOrDefault();
                message.Status = MessageStatus.Failed;
                message.ErrorCode = err?.Code?.ToString(CultureInfo.InvariantCulture);
                message.ErrorMessage = err?.Message ?? err?.Title ?? message.ErrorMessage;
                message.StatusAt = statusAt;

                // Undeliverable recipient (131026): the number isn't on WhatsApp. Stamp the contact invalid
                // so future sends skip it. This is the only registration signal the Cloud API gives us, and
                // it usually arrives here (async status webhook) rather than on the original send response.
                if (MetaPolicyErrorCodes.IsUndeliverableRecipient(message.ErrorCode))
                    await MarkContactNotOnWhatsAppAsync(message.ContactId, ct);

                // Refund the quota ONLY when Meta did not bill this message. Pricing (captured above into
                // message.Billable) is authoritative: if Meta charged for it, crediting the quota back would
                // under-count real usage. Credit the EXACT subscription that was metered (recorded at send
                // time); fall back to the company's current active sub only for legacy rows that predate the
                // MeteredSubscriptionId column.
                var metaBilled = message.Billable == true;
                if (!metaBilled)
                {
                    var refundSubId = message.MeteredSubscriptionId
                        ?? await db.Subscriptions.IgnoreQueryFilters()
                            .Where(sub => sub.CompanyId == message.CompanyId && sub.Status == SubscriptionStatus.Active)
                            .OrderByDescending(sub => sub.CreatedAt)
                            .Select(sub => (Guid?)sub.Id)
                            .FirstOrDefaultAsync(ct);

                    if (refundSubId is not null)
                        refundsBySubscription[refundSubId.Value] =
                            refundsBySubscription.GetValueOrDefault(refundSubId.Value) + 1;
                }

                if (message.CampaignId.HasValue)
                    campaignMessageUpdates[message.Id] = (MessageStatus.Failed, message.ErrorCode);
                continue;
            }

            if (!TryMap(s.Status, out var incoming))
            {
                logger.LogInformation("Unknown delivery status '{Status}' for wamid {Wamid} — ignored.", s.Status, s.Id);
                continue;
            }

            // Forward-only: never resurrect a Failed message; never regress to an equal/earlier rank.
            if (message.Status == MessageStatus.Failed)
                continue;
            if (Rank[incoming] <= Rank.GetValueOrDefault(message.Status, 0))
                continue;

            message.Status = incoming;
            message.StatusAt = statusAt;
            if (message.CampaignId.HasValue)
                campaignMessageUpdates[message.Id] = (incoming, null);
        }

        // Mirror delivery status advances onto CampaignRecipient rows (PRD §8.5 delivery tracking).
        // Pass in-memory status so we don't re-read stale DB values.
        if (campaignMessageUpdates.Count > 0)
            await SyncCampaignRecipientsAsync(campaignMessageUpdates, ct);

        // Credit failed/undelivered messages back to quota.
        if (refundsBySubscription.Count > 0)
            await RefundQuotaAsync(refundsBySubscription, ct);
    }

    /// <summary>
    /// Credits failed/undelivered messages back to the exact subscription each was metered against.
    /// Uses an ATOMIC SQL decrement (<c>SET x = GREATEST(0, x - n)</c>), NOT a tracked read-modify-write:
    /// the previous tracked write read the counter into memory and wrote back an absolute value, which
    /// silently clobbered concurrent atomic meter increments (a send running on another connection between
    /// the refund's read and its commit was lost — free messages). This decrement enlists in the explicit
    /// transaction opened by <see cref="Processing.WebhookProcessingJob"/> around dispatch+MarkProcessed, so
    /// it still commits together with the message's Failed status: exactly-once (a retry sees the message
    /// already Failed and skips), and a rolled-back batch reverts the decrement too. Floored at 0 in SQL.
    /// </summary>
    private async Task RefundQuotaAsync(Dictionary<Guid, int> refundsBySubscription, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var isRelational = db.Database.IsRelational();
        foreach (var (subscriptionId, count) in refundsBySubscription)
        {
            if (count <= 0)
                continue;

            if (isRelational)
            {
                // Atomic, floored decrement — the whole point of the fix. Enlists in the webhook transaction.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE ""Subscriptions""
                       SET ""MessagesUsedThisPeriod"" = GREATEST(0, ""MessagesUsedThisPeriod"" - {count}),
                           ""UpdatedAt"" = {now}
                       WHERE ""Id"" = {subscriptionId}", ct);
            }
            else
            {
                // The EF InMemory provider (unit tests) supports neither raw SQL nor ExecuteUpdate — fall
                // back to a tracked update, flushed by the caller's SaveChanges. Prod is always relational.
                var sub = await db.Subscriptions.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
                if (sub is not null)
                    sub.MessagesUsedThisPeriod = Math.Max(0, sub.MessagesUsedThisPeriod - count);
            }
        }
    }

    private async Task SyncCampaignRecipientsAsync(
        Dictionary<Guid, (MessageStatus Status, string? ErrorCode)> messageUpdates,
        CancellationToken ct)
    {
        var messageIds = messageUpdates.Keys.ToList();

        // Load tracked (no AsNoTracking) so upstream SaveChanges persists recipient status changes.
        var recipients = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Where(r => r.MessageId.HasValue && messageIds.Contains(r.MessageId!.Value))
            .ToListAsync(ct);

        foreach (var recipient in recipients)
        {
            var (msgStatus, errorCode) = messageUpdates[recipient.MessageId!.Value];

            var targetStatus = msgStatus switch
            {
                MessageStatus.Delivered => RecipientStatus.Delivered,
                MessageStatus.Read => RecipientStatus.Read,
                MessageStatus.Failed => RecipientStatus.Failed,
                _ => RecipientStatus.Sent
            };

            // Forward-only — never regress.
            var currentRank = RecipientRank(recipient.Status);
            var targetRank = RecipientRank(targetStatus);
            if (targetRank <= currentRank) continue;

            recipient.Status = targetStatus;
            if (msgStatus == MessageStatus.Failed)
                recipient.ErrorCode ??= errorCode;
        }
    }

    /// <summary>
    /// Mark a contact as not on WhatsApp so future sends skip it. Webhook scope has no tenant context,
    /// so we read across tenants with IgnoreQueryFilters and only stamp once (idempotent on duplicate
    /// callbacks). The change is persisted by the caller's SaveChanges, like message/recipient updates.
    /// </summary>
    private async Task MarkContactNotOnWhatsAppAsync(Guid contactId, CancellationToken ct)
    {
        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == contactId, ct);

        if (contact is null || !contact.IsWhatsAppValid)
            return;

        contact.IsWhatsAppValid = false;
        contact.WhatsAppInvalidAt = DateTime.UtcNow;
        logger.LogInformation(
            "Contact {ContactId} marked not on WhatsApp (code 131026) — will be skipped in future sends.",
            contactId);
    }

    private static int RecipientRank(RecipientStatus s) => s switch
    {
        RecipientStatus.Queued => 0,
        RecipientStatus.Sent => 1,
        RecipientStatus.Delivered => 2,
        RecipientStatus.Read => 3,
        RecipientStatus.Failed => 4,
        RecipientStatus.Skipped => 4,
        _ => 0
    };

    private static bool TryMap(string? metaStatus, out MessageStatus status)
    {
        switch (metaStatus?.ToLowerInvariant())
        {
            case "sent": status = MessageStatus.Sent; return true;
            case "delivered": status = MessageStatus.Delivered; return true;
            case "read": status = MessageStatus.Read; return true;
            default: status = default; return false;
        }
    }

    private static DateTime ParseTimestamp(string? ts)
        => long.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime
            : DateTime.UtcNow;
}
