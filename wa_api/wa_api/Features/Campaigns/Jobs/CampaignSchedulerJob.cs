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
    /// Backstop for the permanent-stall failure mode: a campaign left <see cref="CampaignStatus.Running"/>
    /// with Queued recipients but whose Hangfire job is no longer alive (e.g. a genuine fault exhausted the
    /// batch job's retry budget and it was deleted). Re-enqueues a fresh batch. The liveness check prevents
    /// enqueueing a second batch alongside one that is still Enqueued/Scheduled/Processing, which would
    /// otherwise risk a double-send.
    /// </summary>
    private async Task RecoverStalledCampaignsAsync(AppDbContext db)
    {
        var running = await db.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Running
                        && c.IsActive
                        && c.Recipients.Any(r => r.Status == RecipientStatus.Queued))
            .ToListAsync();

        var recovered = false;
        foreach (var campaign in running)
        {
            if (IsJobAlive(campaign.HangfireJobId)) continue;

            var jobId = jobClient.Enqueue<CampaignBatchSendJob>(
                j => j.RunAsync(campaign.Id, CancellationToken.None));
            campaign.HangfireJobId = jobId;
            recovered = true;

            logger.LogWarning(
                "CampaignSchedulerJob: campaign {Id} was Running with queued recipients but no live job — re-enqueued batch {JobId}.",
                campaign.Id, jobId);
        }

        if (recovered) await db.SaveChangesAsync();
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
