using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.ContactLists.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Snapshots the contact list into CampaignRecipient rows, updates TotalRecipients,
/// and chains into CampaignBatchSendJob.
///
/// Runs cross-tenant (Hangfire has no HttpContext): loads campaign via IgnoreQueryFilters,
/// stamps CompanyId manually on every insert (AuditInterceptor skips when CompanyId != Guid.Empty).
/// </summary>
public class CampaignLaunchJob(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient jobClient,
    ILogger<CampaignLaunchJob> logger)
{
    public async Task RunAsync(Guid campaignId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var campaign = await db.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.Template)
            .Include(c => c.ContactList)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
        {
            logger.LogWarning("CampaignLaunchJob: campaign {Id} not found.", campaignId);
            return;
        }

        if (campaign.Status is CampaignStatus.Cancelled or CampaignStatus.Completed)
        {
            logger.LogInformation("CampaignLaunchJob: campaign {Id} is already in terminal state {Status}, skipping.",
                campaignId, campaign.Status);
            return;
        }

        // Re-validate template is still approved at fire time.
        if (campaign.Template.Status != TemplateStatus.Approved)
        {
            logger.LogError("CampaignLaunchJob: campaign {Id} template {TemplateId} is not Approved ({Status}). Marking campaign Failed.",
                campaignId, campaign.TemplateId, campaign.Template.Status);
            campaign.Status = CampaignStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Load contact IDs from the list. All active members are included.
        var contactIds = await db.ContactListMembers
            .IgnoreQueryFilters()
            .Where(m => m.ContactListId == campaign.ContactListId && m.IsActive)
            .Select(m => m.ContactId)
            .ToListAsync(ct);

        if (contactIds.Count == 0)
        {
            logger.LogWarning("CampaignLaunchJob: campaign {Id} has no contacts in list {ListId}. Marking Completed.",
                campaignId, campaign.ContactListId);
            campaign.Status = CampaignStatus.Completed;
            campaign.TotalRecipients = 0;
            campaign.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Bulk-insert CampaignRecipient rows (skip any that already exist to be idempotent on retry).
        var existingContactIds = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Where(r => r.CampaignId == campaignId)
            .Select(r => r.ContactId)
            .ToListAsync(ct);

        var existingSet = existingContactIds.ToHashSet();
        var newRecipients = contactIds
            .Where(cid => !existingSet.Contains(cid))
            .Select(cid => new CampaignRecipient
            {
                CompanyId = campaign.CompanyId,   // manual stamp — no HttpContext
                CampaignId = campaignId,
                ContactId = cid,
                Status = RecipientStatus.Queued,
                IdempotencyKey = $"campaign:{campaignId}:contact:{cid}",
                ResolvedVariables = "{}",
            })
            .ToList();

        if (newRecipients.Count > 0)
            db.CampaignRecipients.AddRange(newRecipients);

        campaign.Status = CampaignStatus.Running;
        campaign.TotalRecipients = existingSet.Count + newRecipients.Count;
        campaign.LaunchedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("CampaignLaunchJob: campaign {Id} launched with {Count} recipients.",
            campaignId, campaign.TotalRecipients);

        // Chain into batch sender.
        jobClient.Enqueue<CampaignBatchSendJob>(j => j.RunAsync(campaignId, CancellationToken.None));
    }
}
