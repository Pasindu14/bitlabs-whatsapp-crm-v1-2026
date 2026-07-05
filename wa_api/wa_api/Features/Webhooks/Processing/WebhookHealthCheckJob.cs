using Microsoft.EntityFrameworkCore;
using wa_api.Features.Webhooks.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Recurring watchdog that catches a wedged webhook pipeline. A single deploy once broke every webhook for
/// ~13 hours (a bare transaction under the retry execution strategy threw on every dispatch); nothing alerted,
/// so campaign messages silently froze at their initial status while Meta's delivery/read/failed callbacks
/// dead-lettered. This job turns that class of outage into a loud, greppable <c>LogCritical</c> within minutes.
///
/// It flags two independent symptoms:
///  • recent dead-letters — any row that exhausted its retries in the lookback window means dispatch is
///    actively failing (the exact signature of the incident above);
///  • a stale backlog — rows still Received/Processing well past the point the sweeper should have drained or
///    reclaimed them, i.e. work is arriving but not completing.
///
/// The critical log line carries a stable <c>WEBHOOK_PROCESSING_STALLED</c> marker so log-based alerting
/// (netdata on the host, or any log drain) can page on it. Read-only; never mutates rows.
/// </summary>
public sealed class WebhookHealthCheckJob(IServiceScopeFactory scopeFactory, ILogger<WebhookHealthCheckJob> logger)
{
    /// <summary>A row still un-drained this long past ingest is stuck: healthy rows process in seconds and the
    /// sweeper reclaims genuinely stalled ones at the 5-minute processing lease, so 15 minutes is well clear of
    /// normal operation and of one full sweeper cycle.</summary>
    private static readonly TimeSpan BacklogAge = TimeSpan.FromMinutes(15);

    /// <summary>Window for "recent" dead-letters. A row only dead-letters after exhausting its retry budget
    /// (~20 min of backoff), so anything dead in the last 30 minutes reflects a live, ongoing failure.</summary>
    private static readonly TimeSpan DeadLookback = TimeSpan.FromMinutes(30);

    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var staleBacklog = await db.WhatsAppWebhookEvents.IgnoreQueryFilters()
            .CountAsync(e => (e.Status == WebhookEventStatus.Received || e.Status == WebhookEventStatus.Processing)
                             && e.ReceivedAt < now - BacklogAge);

        var recentDead = await db.WhatsAppWebhookEvents.IgnoreQueryFilters()
            .CountAsync(e => e.Status == WebhookEventStatus.Dead && e.UpdatedAt > now - DeadLookback);

        if (recentDead > 0 || staleBacklog > 0)
        {
            logger.LogCritical(
                "WEBHOOK_PROCESSING_STALLED: {RecentDead} webhook(s) dead-lettered in the last {DeadMin}m and " +
                "{StaleBacklog} still unprocessed >{BacklogMin}m. Delivery/read/failed statuses are not being " +
                "applied — messages will freeze at Accepted/Sent. Investigate WebhookProcessingJob now.",
                recentDead, (int)DeadLookback.TotalMinutes, staleBacklog, (int)BacklogAge.TotalMinutes);
        }
        else
        {
            logger.LogInformation("WebhookHealthCheck: OK — no dead-letters, no stale backlog.");
        }
    }
}
