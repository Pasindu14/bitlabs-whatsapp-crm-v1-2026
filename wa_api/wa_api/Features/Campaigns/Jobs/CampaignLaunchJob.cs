using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Snapshots the campaign's targeted contacts (from one or more lists and/or individual picks)
/// into CampaignRecipient rows, updates TotalRecipients, and chains into CampaignBatchSendJob.
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

        if (campaign.Template.Status != TemplateStatus.Approved)
        {
            logger.LogError("CampaignLaunchJob: campaign {Id} template {TemplateId} is not Approved ({Status}). Marking campaign Failed.",
                campaignId, campaign.TemplateId, campaign.Template.Status);
            campaign.Status = CampaignStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Gather contacts from all targeted lists (union across lists, deduplicate).
        var listIds = await db.CampaignContactLists
            .IgnoreQueryFilters()
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactListId)
            .ToListAsync(ct);

        var contactIdsFromLists = listIds.Count > 0
            ? await db.ContactListMembers
                .IgnoreQueryFilters()
                .Where(m => listIds.Contains(m.ContactListId) && m.IsActive)
                .Select(m => m.ContactId)
                .Distinct()
                .ToListAsync(ct)
            : new List<Guid>();

        // Gather individually selected contacts.
        var individualContactIds = await db.CampaignContacts
            .IgnoreQueryFilters()
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactId)
            .ToListAsync(ct);

        // Union both sources, deduplicate.
        var contactIds = contactIdsFromLists
            .Concat(individualContactIds)
            .Distinct()
            .ToList();

        if (contactIds.Count == 0)
        {
            logger.LogWarning("CampaignLaunchJob: campaign {Id} has no contacts. Marking Completed.", campaignId);
            campaign.Status = CampaignStatus.Completed;
            campaign.TotalRecipients = 0;
            campaign.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Bulk-insert CampaignRecipient rows (skip any that already exist — idempotent on retry).
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
                CompanyId = campaign.CompanyId,
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

        jobClient.Enqueue<CampaignBatchSendJob>(j => j.RunAsync(campaignId, CancellationToken.None));
    }
}
