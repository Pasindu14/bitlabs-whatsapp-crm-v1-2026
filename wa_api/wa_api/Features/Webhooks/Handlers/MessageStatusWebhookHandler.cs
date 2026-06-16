using System.Globalization;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages.Entities;
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
    // Sent < Delivered < Read (PRD §5: forward-only StatusRank). Failed is terminal, handled separately.
    private static readonly Dictionary<MessageStatus, int> Rank = new()
    {
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

        foreach (var s in ctx.Change.Value!.Statuses!)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Status))
                continue;

            var message = await db.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ExternalMessageId == s.Id, ct);
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
                var err = s.Errors?.FirstOrDefault();
                message.Status = MessageStatus.Failed;
                message.ErrorCode = err?.Code?.ToString(CultureInfo.InvariantCulture);
                message.ErrorMessage = err?.Message ?? err?.Title ?? message.ErrorMessage;
                message.StatusAt = statusAt;
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
