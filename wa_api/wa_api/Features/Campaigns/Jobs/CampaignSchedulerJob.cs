using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Recurring job (every minute) that finds due Scheduled campaigns and fires CampaignLaunchJob.
/// Handles one-time campaigns that fired their ScheduledAt time and recurring campaigns
/// (which have their next ScheduledAt set by this job after each launch).
/// </summary>
public class CampaignSchedulerJob(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient jobClient,
    ILogger<CampaignSchedulerJob> logger)
{
    // Serialize ticks: this recurring job runs every minute, but if one run overruns 60s (many due
    // campaigns / slow DB) the next tick must NOT start alongside it. Two concurrent ticks can each read the
    // same campaign as Scheduled and both enqueue a launch (→ two batches → double-send). The lock makes
    // ticks strictly sequential; the atomic status flip below is the second line of defense.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var dueCampaigns = await db.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Scheduled
                        && c.ScheduleType == ScheduleType.OneTime
                        && c.ScheduledAt.HasValue
                        && c.ScheduledAt <= now
                        && c.IsActive)
            .ToListAsync();

        if (dueCampaigns.Count > 0)
        {
            logger.LogInformation("CampaignSchedulerJob: {Count} campaign(s) due at {Time:o}.", dueCampaigns.Count, now);

            foreach (var campaign in dueCampaigns)
            {
                var jobId = jobClient.Enqueue<CampaignLaunchJob>(
                    j => j.RunAsync(campaign.Id, CancellationToken.None));

                campaign.Status = CampaignStatus.Running;
                campaign.HangfireJobId = jobId;

                logger.LogInformation("CampaignSchedulerJob: enqueued launch job {JobId} for campaign {Id}.",
                    jobId, campaign.Id);
            }

            await db.SaveChangesAsync();
        }

        await RecoverStalledCampaignsAsync(db);
    }

    /// <summary>
    /// How many consecutive stall-recoveries with NO progress (the Queued backlog didn't shrink) the sweeper
    /// tolerates before concluding the batch is a poison job — a deterministic fault that throws every attempt
    /// — and handing the campaign to <see cref="CampaignPoisonHandlerJob"/> instead of re-enqueueing it again.
    /// Each recovery corresponds to one full batch death (5 exhausted retries), so this is ~3 poison cycles.
    /// </summary>
    private const int MaxNoProgressRecoveries = 3;

    public enum RecoveryDecision { ReEnqueue, Poison }

    /// <summary>
    /// Pure decision for one stall-recovery pass, extracted so it can be unit-tested without Hangfire. Given
    /// the campaign's current strike count, the Queued backlog seen at the previous recovery, and the backlog
    /// now: a shrinking backlog means the batch is making progress, so the strike count resets to zero before
    /// this pass counts. The pass always adds one strike; once the total exceeds
    /// <see cref="MaxNoProgressRecoveries"/> the campaign is poison and must be failed rather than re-enqueued.
    /// </summary>
    public static (int attempts, RecoveryDecision decision) EvaluateRecovery(
        int currentAttempts, int? lastQueuedCount, int queuedNow)
    {
        var attempts = currentAttempts;
        if (lastQueuedCount is int prev && queuedNow < prev)
            attempts = 0;   // backlog shrank → progress → forgive prior strikes

        attempts++;
        var decision = attempts > MaxNoProgressRecoveries
            ? RecoveryDecision.Poison
            : RecoveryDecision.ReEnqueue;
        return (attempts, decision);
    }

    /// <summary>
    /// Backstop for the permanent-stall failure mode: a campaign left <see cref="CampaignStatus.Running"/>
    /// with Queued recipients but whose Hangfire job is no longer alive (e.g. a genuine fault exhausted the
    /// batch job's retry budget and it was deleted). Re-enqueues a fresh batch. The liveness check prevents
    /// enqueueing a second batch alongside one that is still Enqueued/Scheduled/Processing, which would
    /// otherwise risk a double-send.
    ///
    /// A deterministic fault would make every re-enqueue die the same way, so the sweeper would churn forever
    /// and never reach a terminal state. To bound that, each recovery is scored for progress: if the Queued
    /// backlog shrank since the previous recovery the batch is making headway and the no-progress counter
    /// resets; if it hasn't shrunk across <see cref="MaxNoProgressRecoveries"/> consecutive recoveries the
    /// campaign is poison — hand it to <see cref="CampaignPoisonHandlerJob"/> (Failed + tenant notified + any
    /// remaining Queued rows flipped to Failed) rather than re-enqueue it again.
    /// </summary>
    private async Task RecoverStalledCampaignsAsync(AppDbContext db)
    {
        var running = await db.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Running
                        && c.IsActive
                        && c.Recipients.Any(r => r.Status == RecipientStatus.Queued))
            .Select(c => new
            {
                Campaign = c,
                QueuedCount = c.Recipients.Count(r => r.Status == RecipientStatus.Queued),
            })
            .ToListAsync();

        var changed = false;
        foreach (var row in running)
        {
            var campaign = row.Campaign;
            if (IsJobAlive(campaign.HangfireJobId)) continue;

            var (attempts, decision) = EvaluateRecovery(
                campaign.RecoveryAttempts, campaign.LastRecoveryQueuedCount, row.QueuedCount);
            campaign.RecoveryAttempts = attempts;
            campaign.LastRecoveryQueuedCount = row.QueuedCount;
            changed = true;

            if (decision == RecoveryDecision.Poison)
            {
                // Poison: the backlog hasn't moved across repeated recoveries. Stop churning — mark the
                // campaign Failed and notify the tenant. Clear HangfireJobId so a re-enqueue can't race the
                // handler; the handler flips it to Failed which drops it out of this sweeper's query.
                campaign.HangfireJobId = null;
                jobClient.Enqueue<CampaignPoisonHandlerJob>(j => j.RunAsync(campaign.Id));

                logger.LogError(
                    "CampaignSchedulerJob: campaign {Id} made no progress across {Attempts} recoveries " +
                    "({Queued} recipients still Queued) — treating as poison, handing to CampaignPoisonHandlerJob.",
                    campaign.Id, campaign.RecoveryAttempts, row.QueuedCount);
                continue;
            }

            var jobId = jobClient.Enqueue<CampaignBatchSendJob>(
                j => j.RunAsync(campaign.Id, CancellationToken.None));
            campaign.HangfireJobId = jobId;

            logger.LogWarning(
                "CampaignSchedulerJob: campaign {Id} was Running with queued recipients but no live job — " +
                "re-enqueued batch {JobId} (recovery {Attempts}/{Max}).",
                campaign.Id, jobId, campaign.RecoveryAttempts, MaxNoProgressRecoveries);
        }

        if (changed) await db.SaveChangesAsync();
    }

    /// <summary>True when the Hangfire job's most recent state is one that will still execute.</summary>
    private static bool IsJobAlive(string? jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return false;

        var details = JobStorage.Current.GetMonitoringApi().JobDetails(jobId);
        if (details?.History is null || details.History.Count == 0) return false;

        // Hangfire returns state history newest-first; the first entry is the current state.
        var current = details.History[0].StateName;
        return current is "Enqueued" or "Scheduled" or "Processing" or "Awaiting";
    }
}
