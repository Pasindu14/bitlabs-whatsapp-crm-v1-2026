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

        if (dueCampaigns.Count == 0) return;

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
}
