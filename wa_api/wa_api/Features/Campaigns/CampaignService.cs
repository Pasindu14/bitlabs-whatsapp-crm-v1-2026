using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Campaigns.Dtos;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Campaigns.Jobs;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns;

public class CampaignService(
    AppDbContext db,
    IBackgroundJobClient jobClient,
    IRecurringJobManager recurringJobs,
    ISubscriptionGate subscriptionGate,
    ILogger<CampaignService> logger)
    : ICampaignService
{
    public async Task<(IReadOnlyList<CampaignResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? statusFilter, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Campaigns.AsNoTracking()
            .Where(c => c.IsActive)
            .Include(c => c.Template)
            .Include(c => c.ContactLists).ThenInclude(cl => cl.ContactList)
            .Include(c => c.IndividualContacts)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, term));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<CampaignStatus>(statusFilter, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        var total = await query.CountAsync(ct);
        var campaigns = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = campaigns.Select(MapToResponse).ToList();
        return (items, total);
    }

    public async Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.AsNoTracking()
            .Include(c => c.Template)
            .Include(c => c.ContactLists).ThenInclude(cl => cl.ContactList)
            .Include(c => c.IndividualContacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default)
    {
        await ValidateTargetsAsync(request.TemplateId, request.ContactListIds, request.ContactIds, ct);
        ValidateSchedule(request.ScheduleType, request.ScheduledAt, request.RecurrenceCron);

        if (request.ScheduleType != ScheduleType.Recurring)
        {
            var recipientCount = await EstimateRecipientCountAsync(request.ContactListIds, request.ContactIds, ct);
            await subscriptionGate.EnsureCanSendBatchAsync(recipientCount, ct);
        }

        var campaign = new Campaign
        {
            Name = request.Name,
            TemplateId = request.TemplateId,
            VariableMapping = request.VariableMapping ?? "{}",
            ScheduleType = request.ScheduleType,
            ScheduledAt = request.ScheduledAt.HasValue ? DateTime.SpecifyKind(request.ScheduledAt.Value, DateTimeKind.Utc) : null,
            RecurrenceCron = request.RecurrenceCron,
            OverrideConsentGate = request.OverrideConsentGate ?? false,
            Status = CampaignStatus.Draft,
        };

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        await SaveTargetsAsync(campaign.Id, campaign.CompanyId, request.ContactListIds, request.ContactIds, ct);

        return await GetByIdAsync(campaign.Id, ct);
    }

    public async Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status != CampaignStatus.Draft)
            throw new BusinessRuleException("CAMPAIGN_NOT_EDITABLE",
                "Only Draft campaigns can be edited.");

        await ValidateTargetsAsync(request.TemplateId, request.ContactListIds, request.ContactIds, ct);
        ValidateSchedule(request.ScheduleType, request.ScheduledAt, request.RecurrenceCron);

        campaign.Name = request.Name;
        campaign.TemplateId = request.TemplateId;
        campaign.VariableMapping = request.VariableMapping ?? "{}";
        campaign.ScheduleType = request.ScheduleType;
        campaign.ScheduledAt = request.ScheduledAt.HasValue ? DateTime.SpecifyKind(request.ScheduledAt.Value, DateTimeKind.Utc) : null;
        campaign.RecurrenceCron = request.RecurrenceCron;
        campaign.OverrideConsentGate = request.OverrideConsentGate ?? false;

        await db.SaveChangesAsync(ct);

        // Replace join rows with the new selection.
        await db.CampaignContactLists.Where(x => x.CampaignId == id).ExecuteDeleteAsync(ct);
        await db.CampaignContacts.Where(x => x.CampaignId == id).ExecuteDeleteAsync(ct);
        await SaveTargetsAsync(id, campaign.CompanyId, request.ContactListIds, request.ContactIds, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.Cancelled))
            throw new BusinessRuleException("CAMPAIGN_NOT_DELETABLE",
                "Only Draft or Cancelled campaigns can be deleted.");

        campaign.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<CampaignResponse> LaunchAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns
            .Include(c => c.Template).ThenInclude(t => t.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.Scheduled))
            throw new BusinessRuleException("CAMPAIGN_NOT_LAUNCHABLE",
                "Only Draft or Scheduled campaigns can be launched.");

        if (campaign.Template.Status != TemplateStatus.Approved)
            throw new BusinessRuleException("TEMPLATE_NOT_APPROVED",
                "The campaign template must be Approved before launching.");

        if (campaign.Template.WabaConnection.QualityRating == "RED")
            throw new BusinessRuleException("WABA_QUALITY_RED",
                "Cannot launch campaign — your WhatsApp number has a RED quality rating. "
                + "Resolve the account quality issues in Meta Business Suite before sending.");

        var hasTargets = await db.CampaignContactLists.AnyAsync(x => x.CampaignId == id, ct)
            || await db.CampaignContacts.AnyAsync(x => x.CampaignId == id, ct);
        if (!hasTargets)
            throw new BusinessRuleException("NO_RECIPIENTS",
                "Add at least one contact list or individual contact before launching.");

        // UAE quiet-hours: if this launch sends immediately (Immediate, or a OneTime whose time has
        // already passed) the send moment is *now*, so it must be inside the window. A future OneTime
        // and Recurring were already window-validated at create/update; the batch job's runtime gate
        // is the backstop that also stops a long send from spilling past 20:00.
        var sendsNow = campaign.ScheduleType == ScheduleType.Immediate
            || (campaign.ScheduleType == ScheduleType.OneTime
                && campaign.ScheduledAt is { } at && at <= DateTime.UtcNow);
        if (sendsNow && !SendWindow.IsOpen(DateTime.UtcNow))
            throw new BusinessRuleException("OUTSIDE_SEND_WINDOW",
                $"Sends are only permitted between {SendWindow.WindowText}. Schedule this campaign for a time inside that window.");

        // Quota pre-check: skip for Recurring campaigns (fire at future times when quota may have reset).
        if (campaign.ScheduleType != ScheduleType.Recurring)
        {
            var recipientCount = await EstimateRecipientCountAsync(id, ct);
            await subscriptionGate.EnsureCanSendBatchAsync(recipientCount, ct);
        }

        // Compliance audit: launching with the consent gate overridden is a deliberate, risky act.
        // Record it so there's a trail of who sent to non-opted-in contacts and when.
        if (campaign.OverrideConsentGate)
        {
            logger.LogWarning(
                "Campaign {CampaignId} (company {CompanyId}) launched with CONSENT GATE OVERRIDDEN — "
                + "messages will be sent to contacts without recorded opt-in.",
                campaign.Id, campaign.CompanyId);
        }

        // Atomically claim the launch: flip out of the launchable set in a single UPDATE so two concurrent
        // launches (or a launch racing the scheduler) can't both pass the Status check above and enqueue two
        // batches → a double-send (M3). Only the launcher that changes exactly one row proceeds; the loser
        // gets a 409. The tracked entity's final status is set and persisted below.
        var claimed = await db.Campaigns
            .Where(c => c.Id == id
                && (c.Status == CampaignStatus.Draft || c.Status == CampaignStatus.Scheduled))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Running), ct);
        if (claimed == 0)
            throw new ConflictException("CAMPAIGN_ALREADY_LAUNCHING",
                "This campaign is already being launched or is no longer in a launchable state.");

        string jobId;
        if (campaign.ScheduleType == ScheduleType.OneTime && campaign.ScheduledAt.HasValue)
        {
            var delay = campaign.ScheduledAt.Value - DateTime.UtcNow;
            jobId = delay > TimeSpan.Zero
                ? jobClient.Schedule<CampaignLaunchJob>(j => j.RunAsync(id, CancellationToken.None), delay)
                : jobClient.Enqueue<CampaignLaunchJob>(j => j.RunAsync(id, CancellationToken.None));
            campaign.Status = delay > TimeSpan.Zero ? CampaignStatus.Scheduled : CampaignStatus.Running;
        }
        else if (campaign.ScheduleType == ScheduleType.Recurring && !string.IsNullOrWhiteSpace(campaign.RecurrenceCron))
        {
            var recurringId = $"campaign-recurring-{id}";
            recurringJobs.AddOrUpdate<CampaignLaunchJob>(
                recurringId,
                j => j.RunAsync(id, CancellationToken.None),
                campaign.RecurrenceCron);
            jobId = recurringId;
            campaign.Status = CampaignStatus.Scheduled;
        }
        else
        {
            jobId = jobClient.Enqueue<CampaignLaunchJob>(j => j.RunAsync(id, CancellationToken.None));
            campaign.Status = CampaignStatus.Running;
        }

        campaign.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<CampaignResponse> PauseAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status != CampaignStatus.Running)
            throw new BusinessRuleException("CAMPAIGN_NOT_RUNNING", "Only Running campaigns can be paused.");

        if (campaign.HangfireJobId is not null)
        {
            try { BackgroundJob.Delete(campaign.HangfireJobId); } catch { /* best-effort */ }
        }

        campaign.Status = CampaignStatus.Paused;
        campaign.HangfireJobId = null;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<CampaignResponse> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status != CampaignStatus.Paused)
            throw new BusinessRuleException("CAMPAIGN_NOT_PAUSED", "Only Paused campaigns can be resumed.");

        var queuedCount = await db.CampaignRecipients
            .CountAsync(r => r.CampaignId == id && r.Status == RecipientStatus.Queued, ct);
        await subscriptionGate.EnsureCanSendBatchAsync(queuedCount, ct);

        var jobId = jobClient.Enqueue<CampaignBatchSendJob>(
            j => j.RunAsync(id, CancellationToken.None));

        campaign.Status = CampaignStatus.Running;
        campaign.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<CampaignResponse> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
            throw new BusinessRuleException("CAMPAIGN_ALREADY_TERMINAL",
                "Campaign is already in a terminal state.");

        if (campaign.HangfireJobId is not null)
        {
            try { BackgroundJob.Delete(campaign.HangfireJobId); } catch { /* best-effort */ }
            if (campaign.ScheduleType == ScheduleType.Recurring)
                recurringJobs.RemoveIfExists($"campaign-recurring-{id}");
        }

        await db.CampaignRecipients
            .Where(r => r.CampaignId == id && r.Status == RecipientStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RecipientStatus.Skipped)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);

        campaign.Status = CampaignStatus.Cancelled;
        campaign.HangfireJobId = null;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<CampaignStatsResponse> GetStatsAsync(Guid id, CancellationToken ct = default)
    {
        var exists = await db.Campaigns.AnyAsync(c => c.Id == id, ct);
        if (!exists) throw new NotFoundException("Campaign", id);

        var counts = await db.CampaignRecipients
            .Where(r => r.CampaignId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Queued = g.Count(r => r.Status == RecipientStatus.Queued),
                Sent = g.Count(r => r.Status == RecipientStatus.Sent),
                Delivered = g.Count(r => r.Status == RecipientStatus.Delivered),
                Read = g.Count(r => r.Status == RecipientStatus.Read),
                Failed = g.Count(r => r.Status == RecipientStatus.Failed),
                Skipped = g.Count(r => r.Status == RecipientStatus.Skipped),
                SentWithoutConsent = g.Count(r => r.SentWithoutConsent),
            })
            .FirstOrDefaultAsync(ct);

        if (counts is null)
            return new CampaignStatsResponse(id, 0, 0, 0, 0, 0, 0, 0, 0, 0m, 0m);

        var delivered = counts.Delivered + counts.Read;
        var deliveryRate = counts.Total > 0 ? Math.Round((decimal)delivered / counts.Total * 100, 1) : 0m;
        var readRate = counts.Total > 0 ? Math.Round((decimal)counts.Read / counts.Total * 100, 1) : 0m;

        return new CampaignStatsResponse(
            id, counts.Total, counts.Queued, counts.Sent,
            counts.Delivered, counts.Read, counts.Failed, counts.Skipped,
            counts.SentWithoutConsent, deliveryRate, readRate);
    }

    public async Task<(IReadOnlyList<CampaignRecipientResponse> Items, int Total)> GetRecipientsAsync(
        Guid id, int page, int pageSize, string? statusFilter, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var exists = await db.Campaigns.AnyAsync(c => c.Id == id, ct);
        if (!exists) throw new NotFoundException("Campaign", id);

        var query = db.CampaignRecipients.AsNoTracking()
            .Include(r => r.Contact)
            .Where(r => r.CampaignId == id);

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<RecipientStatus>(statusFilter, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new CampaignRecipientResponse(
                r.Id, r.ContactId, r.Contact.Name, r.Contact.Phone,
                r.Status, r.ErrorCode, r.SentWithoutConsent, r.MessageId, r.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<CampaignResponse> DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        var source = await db.Campaigns.AsNoTracking()
            .Include(c => c.ContactLists)
            .Include(c => c.IndividualContacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Campaign", id);

        var copy = new Campaign
        {
            Name = $"{source.Name} (Copy)",
            TemplateId = source.TemplateId,
            VariableMapping = source.VariableMapping,
            ScheduleType = source.ScheduleType,
            ScheduledAt = source.ScheduledAt,
            RecurrenceCron = source.RecurrenceCron,
            OverrideConsentGate = source.OverrideConsentGate,
            Status = CampaignStatus.Draft,
        };

        db.Campaigns.Add(copy);
        await db.SaveChangesAsync(ct);

        var listIds = source.ContactLists.Select(x => x.ContactListId).ToList();
        var contactIds = source.IndividualContacts.Select(x => x.ContactId).ToList();
        await SaveTargetsAsync(copy.Id, copy.CompanyId, listIds, contactIds, ct);

        return await GetByIdAsync(copy.Id, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task ValidateTargetsAsync(
        Guid templateId,
        List<Guid>? contactListIds,
        List<Guid>? contactIds,
        CancellationToken ct)
    {
        var templateExists = await db.Templates.AnyAsync(t => t.Id == templateId, ct);
        if (!templateExists) throw new NotFoundException("Template", templateId);

        if (contactListIds is { Count: > 0 })
        {
            foreach (var listId in contactListIds)
            {
                var listExists = await db.ContactLists.AnyAsync(l => l.Id == listId, ct);
                if (!listExists) throw new NotFoundException("ContactList", listId);
            }
        }

        if (contactIds is { Count: > 0 })
        {
            foreach (var contactId in contactIds)
            {
                var contactExists = await db.Contacts.AnyAsync(c => c.Id == contactId, ct);
                if (!contactExists) throw new NotFoundException("Contact", contactId);
            }
        }
    }

    private async Task SaveTargetsAsync(
        Guid campaignId,
        Guid companyId,
        List<Guid>? contactListIds,
        List<Guid>? contactIds,
        CancellationToken ct)
    {
        if (contactListIds is { Count: > 0 })
        {
            var rows = contactListIds.Select(listId => new CampaignContactList
            {
                CompanyId = companyId,
                CampaignId = campaignId,
                ContactListId = listId,
            });
            db.CampaignContactLists.AddRange(rows);
        }

        if (contactIds is { Count: > 0 })
        {
            var rows = contactIds.Select(contactId => new CampaignContact
            {
                CompanyId = companyId,
                CampaignId = campaignId,
                ContactId = contactId,
            });
            db.CampaignContacts.AddRange(rows);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<CampaignAudienceHealthResponse> GetAudienceHealthAsync(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new NotFoundException("Campaign", campaignId);

        // Resolve the distinct audience (lists ∪ individual contacts), same as the launch job.
        var listIds = await db.CampaignContactLists
            .Where(x => x.CampaignId == campaignId).Select(x => x.ContactListId).ToListAsync(ct);
        var individualIds = await db.CampaignContacts
            .Where(x => x.CampaignId == campaignId).Select(x => x.ContactId).ToListAsync(ct);
        var fromLists = listIds.Count > 0
            ? await db.ContactListMembers
                .Where(m => listIds.Contains(m.ContactListId) && m.IsActive)
                .Select(m => m.ContactId).Distinct().ToListAsync(ct)
            : new List<Guid>();
        var contactIds = fromLists.Concat(individualIds).Distinct().ToList();

        // Pull only the flags that drive the send-time gates for the distinct set.
        var flags = contactIds.Count > 0
            ? await db.Contacts
                .Where(c => contactIds.Contains(c.Id))
                .Select(c => new { c.HasOptedIn, c.IsOptedOut, c.IsWhatsAppValid })
                .ToListAsync(ct)
            : new();

        var ov = campaign.OverrideConsentGate;
        int noConsent = 0, optedOut = 0, invalid = 0, sendable = 0, noConsentOverridden = 0;

        // Precedence must match CampaignBatchSendJob exactly: no-consent → opted-out → not-on-WhatsApp.
        foreach (var f in flags)
        {
            if (!f.HasOptedIn && !ov) { noConsent++; continue; }
            if (f.IsOptedOut) { optedOut++; continue; }
            if (!f.IsWhatsAppValid) { invalid++; continue; }
            sendable++;
            if (!f.HasOptedIn) noConsentOverridden++; // sending without recorded consent (override on)
        }

        return new CampaignAudienceHealthResponse(
            flags.Count, sendable, noConsent, optedOut, invalid, noConsentOverridden, ov);
    }

    private async Task<int> EstimateRecipientCountAsync(Guid campaignId, CancellationToken ct)
    {
        var listIds = await db.CampaignContactLists
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactListId)
            .ToListAsync(ct);

        var individualContactIds = await db.CampaignContacts
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactId)
            .ToListAsync(ct);

        return await EstimateRecipientCountAsync(listIds, individualContactIds, ct);
    }

    private async Task<int> EstimateRecipientCountAsync(
        List<Guid>? contactListIds, List<Guid>? contactIds, CancellationToken ct)
    {
        var contactIdsFromLists = contactListIds is { Count: > 0 }
            ? await db.ContactListMembers
                .Where(m => contactListIds.Contains(m.ContactListId) && m.IsActive)
                .Select(m => m.ContactId)
                .Distinct()
                .ToListAsync(ct)
            : new List<Guid>();

        return contactIdsFromLists
            .Concat(contactIds ?? Enumerable.Empty<Guid>())
            .Distinct()
            .Count();
    }

    private static void ValidateSchedule(ScheduleType type, DateTime? scheduledAt, string? cron)
    {
        if (type == ScheduleType.OneTime && scheduledAt is null)
            throw new BusinessRuleException("SCHEDULE_REQUIRED",
                "ScheduledAt is required for OneTime campaigns.");

        if (type == ScheduleType.Recurring && string.IsNullOrWhiteSpace(cron))
            throw new BusinessRuleException("CRON_REQUIRED",
                "RecurrenceCron is required for Recurring campaigns.");

        // UAE quiet-hours: the start time must fall inside 08:00–20:00 UAE. (Immediate has no
        // configured time — its window is checked at launch, against the actual send moment.)
        if (type == ScheduleType.OneTime && scheduledAt is { } at
            && !SendWindow.IsOpen(DateTime.SpecifyKind(at, DateTimeKind.Utc)))
            throw new BusinessRuleException("OUTSIDE_SEND_WINDOW",
                $"Scheduled sends are only permitted between {SendWindow.WindowText}. Pick a time inside that window.");

        if (type == ScheduleType.Recurring && cron is not null && SendWindow.CronStartsInWindow(cron) == false)
            throw new BusinessRuleException("OUTSIDE_SEND_WINDOW",
                $"Recurring sends are only permitted between {SendWindow.WindowText}. Adjust the schedule to fire inside that window.");
    }

    private static CampaignResponse MapToResponse(Campaign c) => new(
        c.Id, c.Name,
        c.TemplateId, c.Template?.Name ?? string.Empty,
        c.ContactLists.Select(x => x.ContactListId).ToList(),
        c.ContactLists.Select(x => x.ContactList?.Name ?? string.Empty).ToList(),
        c.IndividualContacts.Select(x => x.ContactId).ToList(),
        c.VariableMapping, c.Status, c.ScheduleType,
        c.ScheduledAt, c.RecurrenceCron,
        c.TotalRecipients, c.SentCount,
        c.OverrideConsentGate, c.NoConsentSentCount,
        c.LaunchedAt, c.CompletedAt,
        c.CreatedAt, c.UpdatedAt);
}
