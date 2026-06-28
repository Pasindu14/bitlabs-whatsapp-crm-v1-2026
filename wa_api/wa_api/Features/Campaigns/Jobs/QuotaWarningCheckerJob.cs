using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Runs daily at 06:00 UTC. Scans all OneTime Scheduled campaigns firing within the next 72 hours,
/// compares recipient count against remaining quota, and emits a quota-warning notification if
/// the campaign would fail at fire time. Idempotent — one warning per campaign per type.
/// </summary>
public class QuotaWarningCheckerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<QuotaWarningCheckerJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;
        var windowEnd = now.AddHours(72);

        var upcoming = await db.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.ContactLists)
            .Include(c => c.IndividualContacts)
            .Where(c => c.ScheduleType == ScheduleType.OneTime
                     && c.Status == CampaignStatus.Scheduled
                     && c.ScheduledAt > now
                     && c.ScheduledAt <= windowEnd)
            .ToListAsync(ct);

        if (upcoming.Count == 0)
        {
            logger.LogDebug("QuotaWarningCheckerJob: no upcoming OneTime campaigns in the next 72h.");
            return;
        }

        // Load all relevant subscriptions in one query (keyed by CompanyId).
        var companyIds = upcoming.Select(c => c.CompanyId).Distinct().ToList();
        var subscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .Include(s => s.Plan)
            .Where(s => companyIds.Contains(s.CompanyId) && s.Status == Subscriptions.Entities.SubscriptionStatus.Active)
            .ToDictionaryAsync(s => s.CompanyId, ct);

        foreach (var campaign in upcoming)
        {
            if (!subscriptions.TryGetValue(campaign.CompanyId, out var sub) || sub.Plan is null)
                continue;

            int remaining = sub.Plan.MonthlyMessageQuota + sub.ExtraMessageCredits - sub.MessagesUsedThisPeriod;

            int recipients = campaign.TotalRecipients > 0
                ? campaign.TotalRecipients
                : await ResolveDistinctRecipientCountAsync(db, campaign.Id, ct);

            if (recipients <= remaining)
                continue;

            var hoursUntil = (campaign.ScheduledAt!.Value - now).TotalHours;
            var type = hoursUntil switch
            {
                <= 6  => NotificationType.QuotaWarningToday,
                <= 24 => NotificationType.QuotaWarning1Day,
                <= 48 => NotificationType.QuotaWarning2Days,
                _     => NotificationType.QuotaWarning3Days,
            };

            var daysLabel = type == NotificationType.QuotaWarningToday
                ? "today"
                : $"in {(int)Math.Ceiling(hoursUntil / 24)} day(s)";

            var title = $"Campaign \"{campaign.Name}\" sends {daysLabel} — quota insufficient";
            var body  = $"Needs {recipients:N0} messages but only {remaining:N0} remain. " +
                        $"Upgrade your plan or reduce recipients before " +
                        $"{campaign.ScheduledAt:d MMM HH:mm} UTC.";

            await notifier.CreateAsync(campaign.CompanyId, campaign.Id, null, type, title, body, ct);

            logger.LogWarning(
                "QuotaWarningCheckerJob: campaign {Id} ({Name}) fires {ScheduledAt} — needs {Recipients}, has {Remaining}.",
                campaign.Id, campaign.Name, campaign.ScheduledAt, recipients, remaining);
        }
    }

    private static async Task<int> ResolveDistinctRecipientCountAsync(
        AppDbContext db, Guid campaignId, CancellationToken ct)
    {
        var listIds = await db.CampaignContactLists
            .IgnoreQueryFilters()
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactListId)
            .ToListAsync(ct);

        var fromLists = listIds.Count > 0
            ? await db.ContactListMembers
                .IgnoreQueryFilters()
                .Where(m => listIds.Contains(m.ContactListId) && m.IsActive)
                .Select(m => m.ContactId)
                .Distinct()
                .ToListAsync(ct)
            : new List<Guid>();

        var individual = await db.CampaignContacts
            .IgnoreQueryFilters()
            .Where(x => x.CampaignId == campaignId)
            .Select(x => x.ContactId)
            .ToListAsync(ct);

        return fromLists.Concat(individual).Distinct().Count();
    }
}
