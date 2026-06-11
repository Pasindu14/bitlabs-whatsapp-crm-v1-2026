using Hangfire;
using wa_api.Features.Webhooks.Ingestion;

namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Recurring safety net: re-enqueues inbox rows stuck in a non-terminal state (Received/Processing/Failed)
/// older than <see cref="StuckThreshold"/>. Recovers from a lost enqueue (in-memory Hangfire restarted before
/// the job ran) or a worker that crashed mid-process. Bounded per run; Dead rows are left for manual triage.
/// Re-processing is safe because <see cref="WebhookProcessingJob"/> is idempotent.
/// </summary>
public sealed class WebhookSweeperJob(IServiceScopeFactory scopeFactory, ILogger<WebhookSweeperJob> logger)
{
    // Reuse the processing lease so a row is only re-enqueued once its claim is genuinely reclaimable —
    // the two thresholds can't drift apart.
    private static readonly TimeSpan StuckThreshold = WebhookProcessingJob.ProcessingLease;
    private const int BatchSize = 200;

    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWebhookInboxService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();

        var stuck = await inbox.GetStuckIdsAsync(StuckThreshold, BatchSize);
        if (stuck.Count == 0)
            return;

        foreach (var id in stuck)
            jobs.Enqueue<WebhookProcessingJob>(j => j.RunAsync(id, CancellationToken.None));

        logger.LogInformation("WebhookSweeper re-enqueued {Count} stuck webhook event(s).", stuck.Count);
    }
}
