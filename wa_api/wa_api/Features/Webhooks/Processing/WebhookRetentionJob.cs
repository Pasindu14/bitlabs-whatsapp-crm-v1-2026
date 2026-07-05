using Microsoft.EntityFrameworkCore;
using wa_api.Features.Webhooks.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Daily retention sweep for the webhook inbox (M12). Every outbound message spawns 3+ status rows
/// (sent/delivered/read) and every inbound one more, so the table grows unbounded without pruning. Deletes
/// only terminal-SUCCESS rows (<see cref="WebhookEventStatus.Processed"/>) older than <see cref="Retention"/>:
/// <see cref="WebhookEventStatus.Dead"/> rows are kept for triage, and non-terminal rows are still in flight.
/// Bounded per batch and looped a fixed number of times so a large backlog drains over a few runs without a
/// long-held lock. Uses the composite (Status, UpdatedAt) index so each batch is an index range delete.
/// </summary>
public sealed class WebhookRetentionJob(IServiceScopeFactory scopeFactory, ILogger<WebhookRetentionJob> logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    private const int BatchSize = 5000;
    private const int MaxBatchesPerRun = 20;   // ≤ 100k rows/run — a safety ceiling, not a normal load

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow - Retention;
        var total = 0;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            // Bound the DELETE via an ordered id sub-select (ExecuteDelete itself can't take Take/OrderBy).
            var idsToDelete = db.WhatsAppWebhookEvents.IgnoreQueryFilters()
                .Where(e => e.Status == WebhookEventStatus.Processed && e.UpdatedAt < cutoff)
                .OrderBy(e => e.UpdatedAt)
                .Take(BatchSize)
                .Select(e => e.Id);

            var deleted = await db.WhatsAppWebhookEvents.IgnoreQueryFilters()
                .Where(e => idsToDelete.Contains(e.Id))
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < BatchSize)
                break;   // caught up
        }

        if (total > 0)
            logger.LogInformation("WebhookRetentionJob pruned {Count} processed webhook event(s) older than {Days}d.",
                total, Retention.TotalDays);
    }
}
