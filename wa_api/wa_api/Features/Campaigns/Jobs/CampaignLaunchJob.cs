using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
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
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

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
            await notifier.CreateAsync(
                campaign.CompanyId,
                campaign.Id,
                null,
                NotificationType.CampaignFailed,
                $"Campaign \"{campaign.Name}\" failed to launch",
                $"Template is not approved (status: {campaign.Template.Status}). Update the template and re-launch.",
                ct);
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

        // Recurring re-fire: after a run completes, CampaignBatchSendJob resets the campaign to Scheduled and
        // the cron fires this launch again. Every contact already has a CampaignRecipient row, so without this
        // the new-recipient set below is empty and the campaign silently sends nothing ever again. Start a
        // fresh run: bump the run number and re-queue prior recipients that are STILL in the audience (clearing
        // their last-run send state) so this run re-sends them. Each resulting Message is stamped with the new
        // run number, and the batch's per-run duplicate guard is scoped to it, so re-sending is not blocked by
        // the previous run's messages. Guarded on Status==Scheduled so a launch-job retry (which has already
        // flipped the campaign to Running) never advances the run a second time.
        var recipientsExist = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .AnyAsync(r => r.CampaignId == campaignId, ct);

        if (campaign.ScheduleType == ScheduleType.Recurring
            && campaign.Status == CampaignStatus.Scheduled
            && recipientsExist)
        {
            campaign.CurrentRunNumber++;

            // Re-queue recipients still targeted by the campaign so this run re-sends them.
            await db.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(r => r.CampaignId == campaignId && contactIds.Contains(r.ContactId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RecipientStatus.Queued)
                    .SetProperty(r => r.MessageId, (Guid?)null)
                    .SetProperty(r => r.ErrorCode, (string?)null)
                    .SetProperty(r => r.SentWithoutConsent, false)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);

            // Contacts dropped from the audience since the last run must not be re-sent — retire any of their
            // rows still left Queued (terminal rows from the prior run are already excluded by the batch).
            await db.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(r => r.CampaignId == campaignId
                            && !contactIds.Contains(r.ContactId)
                            && r.Status == RecipientStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RecipientStatus.Skipped)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);

            logger.LogInformation(
                "CampaignLaunchJob: recurring campaign {Id} starting run {Run} — re-queued audience.",
                campaignId, campaign.CurrentRunNumber);
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
        campaign.TotalRecipients = contactIds.Count;   // snapshot of THIS run's target audience
        campaign.LaunchedAt = DateTime.UtcNow;

        // Fresh run starts with a clean poison-recovery slate: the sweeper's no-progress strikes from a prior
        // run (or a since-fixed fault) must not carry over and prematurely fail this launch.
        campaign.RecoveryAttempts = 0;
        campaign.LastRecoveryQueuedCount = null;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("CampaignLaunchJob: campaign {Id} launched with {Count} recipients.",
            campaignId, campaign.TotalRecipients);

        // Enqueue the send batch AND record its job id on the campaign. Critical: without persisting this,
        // campaign.HangfireJobId keeps pointing at THIS (launch) job, which flips to Succeeded the moment we
        // return. The CampaignSchedulerJob stall-recovery sweeper then sees a Running campaign with Queued
        // recipients whose HangfireJobId is a dead job, wrongly concludes the batch stalled, and enqueues a
        // SECOND CampaignBatchSendJob — the two run concurrently and double-send every recipient. Pointing
        // HangfireJobId at the live batch job makes the sweeper's IsJobAlive check see it and skip.
        var batchJobId = jobClient.Enqueue<CampaignBatchSendJob>(j => j.RunAsync(campaignId, CancellationToken.None));
        campaign.HangfireJobId = batchJobId;
        await db.SaveChangesAsync(ct);
    }
}
