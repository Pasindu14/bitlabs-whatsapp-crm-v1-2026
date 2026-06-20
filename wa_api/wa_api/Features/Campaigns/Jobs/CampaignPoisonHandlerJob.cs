using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Called when CampaignBatchSendJob permanently fails (max retries exceeded).
/// Marks the campaign as Failed and flips all remaining Queued recipients to Failed.
/// </summary>
public class CampaignPoisonHandlerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CampaignPoisonHandlerJob> logger)
{
    public async Task RunAsync(Guid campaignId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var campaign = await db.Campaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == campaignId);

        if (campaign is null)
        {
            logger.LogWarning("CampaignPoisonHandlerJob: campaign {Id} not found.", campaignId);
            return;
        }

        campaign.Status = CampaignStatus.Failed;
        campaign.HangfireJobId = null;

        var now = DateTime.UtcNow;
        await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RecipientStatus.Failed)
                .SetProperty(r => r.ErrorCode, "CAMPAIGN_FAILED")
                .SetProperty(r => r.UpdatedAt, now));

        await db.SaveChangesAsync();

        await notifier.CreateAsync(
            campaign.CompanyId,
            campaign.Id,
            null,
            NotificationType.CampaignFailed,
            $"Campaign \"{campaign.Name}\" failed",
            "Maximum retries exceeded. Check the campaign logs for details.");

        logger.LogError(
            "CampaignPoisonHandlerJob: campaign {Id} marked Failed — all remaining Queued recipients set to Failed.",
            campaignId);
    }
}
