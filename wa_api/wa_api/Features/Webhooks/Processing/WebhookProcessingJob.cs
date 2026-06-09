using Hangfire;
using wa_api.Features.Webhooks.Entities;
using wa_api.Features.Webhooks.Ingestion;

namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Processes one inbox row: mark Processing → dispatch (handlers mutate the shared context) → mark Processed
/// (a single atomic commit of all handler changes + status). On failure it marks the row Failed (will retry)
/// or Dead (retries exhausted) and rethrows so Hangfire drives the backoff. Runs on the dedicated
/// <c>webhooks</c> queue. Idempotent: a redelivered job whose row is already Processed returns immediately,
/// and the handlers are forward-only.
/// </summary>
public sealed class WebhookProcessingJob(IServiceScopeFactory scopeFactory, ILogger<WebhookProcessingJob> logger)
{
    /// <summary>Max attempts before dead-lettering — kept in sync with the [AutomaticRetry] budget below.</summary>
    private const int MaxAttempts = 5;

    [Queue("webhooks")]
    [AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 10, 30, 90, 300, 900 },
        OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid eventId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWebhookInboxService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        var row = await inbox.GetForProcessingAsync(eventId, ct);
        if (row is null)
        {
            logger.LogWarning("Webhook event {Id} not found — nothing to process.", eventId);
            return;
        }
        if (row.Status == WebhookEventStatus.Processed)
            return; // job redelivered after success — idempotent no-op.

        await inbox.MarkProcessingAsync(row, ct);

        try
        {
            await dispatcher.DispatchAsync(row, ct);
            // Single SaveChanges — flushes every handler mutation together with the Processed status.
            await inbox.MarkProcessedAsync(row, ct);
            logger.LogInformation("Webhook {Id} processed (attempt {Attempt}).", row.Id, row.Attempts);
        }
        catch (Exception ex)
        {
            var dead = row.Attempts >= MaxAttempts;
            await inbox.MarkFailedAsync(row, ex.Message, dead, ct);
            if (dead)
                logger.LogError(ex, "Webhook {Id} dead-lettered after {Attempts} attempt(s).", row.Id, row.Attempts);
            else
                logger.LogWarning(ex, "Webhook {Id} attempt {Attempts} failed — Hangfire will retry.", row.Id, row.Attempts);
            throw; // rethrow so Hangfire records the failure and schedules the backoff retry
        }
    }
}
