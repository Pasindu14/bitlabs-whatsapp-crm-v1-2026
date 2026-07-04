using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Webhooks.Entities;
using wa_api.Features.Webhooks.Ingestion;
using wa_api.Infrastructure.Persistence;

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

    /// <summary>
    /// How long a row's Processing claim is owned before a crashed worker's lease can be reclaimed by
    /// another worker. Must exceed the dispatch wall-clock budget (Meta HttpClient timeout is 15 s) so a
    /// healthy-but-slow dispatch is never stolen mid-flight (which would re-introduce double-processing).
    /// Reused by <c>WebhookSweeperJob</c> as its stuck threshold so the two values can't drift.
    /// </summary>
    public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    [Queue("webhooks")]
    [AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 10, 30, 90, 300, 900 },
        OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid eventId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWebhookInboxService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await inbox.GetForProcessingAsync(eventId, ct);
        if (row is null)
        {
            logger.LogWarning("Webhook event {Id} not found — nothing to process.", eventId);
            return;
        }
        if (row.Status == WebhookEventStatus.Processed)
            return; // job redelivered after success — idempotent no-op.

        // Atomic claim: if another worker already holds the live lease (concurrent run or a sweeper
        // re-enqueue racing the original), bail WITHOUT dispatching. Returning (not throwing) keeps
        // Hangfire from recording a spurious failure/retry for a row that is already being handled.
        if (!await inbox.TryClaimForProcessingAsync(row, ProcessingLease, ct))
        {
            logger.LogInformation("Webhook {Id} already claimed by another worker — skipping.", row.Id);
            return;
        }

        try
        {
            // One explicit transaction around dispatch + MarkProcessed. Handler mutations are tracked and
            // flushed by MarkProcessedAsync's SaveChanges; any atomic SQL a handler runs (e.g. the quota
            // refund's GREATEST(0, x - n) decrement) enlists in this same transaction. So the refund and the
            // message's Failed status commit together — exactly-once (a retry sees the message already Failed
            // and skips), and a rolled-back batch reverts the refund too — while the decrement stays atomic
            // against concurrent meter increments on another connection.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await dispatcher.DispatchAsync(row, ct);
                await inbox.MarkProcessedAsync(row, ct);
                await tx.CommitAsync(ct);
            }
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
