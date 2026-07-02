using Microsoft.EntityFrameworkCore;
using wa_api.Features.AdminReports.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.AdminReports;

/// <summary>
/// Cross-tenant reporting aggregation for the SuperAdmin admin panel — packages
/// purchased by date range, company balances, platform KPIs, and usage analytics.
/// Like <see cref="Monitoring.MonitoringService"/> it intentionally bypasses the global
/// CompanyId query filter via <c>IgnoreQueryFilters()</c> on tenant-scoped tables so every
/// tenant's data is visible. Call sites are restricted to SuperAdmin-only endpoints.
/// Read-only: never calls SaveChanges.
/// </summary>
public class AdminReportsService(AppDbContext db)
{
    /// <summary>Remaining quota below this fraction of the plan quota flags a low balance.</summary>
    private const double LowBalanceThreshold = 0.10;

    // ── Packages purchased ────────────────────────────────────────────────────

    public async Task<PackagesReportResponse> GetPackagesAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (rangeFrom, rangeTo) = NormalizeRange(from, to);

        // One SubscriptionPurchase row per subscribe (the Assign action) — including STACK
        // re-subscribes, which mutate the existing Subscription row rather than creating a new
        // one and would otherwise be invisible. Price/Currency/PlanName are SNAPSHOTTED at
        // purchase time, so this is the source of truth for revenue (immune to later catalog
        // price changes), not the plan's current Plan.Price.
        var purchases = await db.SubscriptionPurchases
            .IgnoreQueryFilters()
            .Where(p => p.CreatedAt >= rangeFrom && p.CreatedAt <= rangeTo)
            .Select(p => new { p.PlanId, p.PlanName, p.Price, p.Currency, p.CreatedAt })
            .ToListAsync(ct);

        var byPlan = purchases
            .GroupBy(p => p.PlanId)
            .Select(g =>
            {
                // Snapshots can differ across purchases of the same plan (e.g. a mid-range
                // price change); show the most recent snapshot as the representative unit
                // price and sum the actual paid prices for revenue.
                var latest = g.OrderByDescending(x => x.CreatedAt).First();
                return new PlanPackageRow(
                    g.Key,
                    latest.PlanName,
                    latest.Price,
                    latest.Currency,
                    g.Count(),
                    g.Sum(x => x.Price)
                );
            })
            .OrderByDescending(r => r.Revenue)
            .ToList();

        var byDay = purchases
            .GroupBy(p => p.CreatedAt.Date)
            .Select(g => new DailyPackageRow(
                g.Key,
                g.Count(),
                g.Sum(x => x.Price)
            ))
            .OrderBy(r => r.Date)
            .ToList();

        return new PackagesReportResponse(
            rangeFrom,
            rangeTo,
            purchases.Count,
            byPlan.Sum(r => r.Revenue),
            byPlan,
            byDay
        );
    }

    // ── Balances ──────────────────────────────────────────────────────────────

    public async Task<BalancesResponse> GetBalancesAsync(CancellationToken ct = default)
    {
        var rows = await GetBalanceRowsAsync(ct);
        return new BalancesResponse(rows);
    }

    /// <summary>Shared by the balances report and the dashboard low-balance count.</summary>
    private async Task<List<BalanceRow>> GetBalanceRowsAsync(CancellationToken ct)
    {
        var subs = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.IsActive && s.Status == SubscriptionStatus.Active)
            .Select(s => new
            {
                s.CompanyId,
                s.PlanId,
                s.MessagesUsedThisPeriod,
                s.ExtraMessageCredits,
                s.CurrentPeriodEnd,
            })
            .ToListAsync(ct);

        var companyNames = await db.Companies
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var nameById = companyNames.ToDictionary(c => c.Id, c => c.Name);

        var plans = await db.Plans
            .Select(p => new { p.Id, p.Name, p.MonthlyMessageQuota })
            .ToListAsync(ct);
        var planById = plans.ToDictionary(p => p.Id);

        return subs.Select(s =>
        {
            planById.TryGetValue(s.PlanId, out var plan);
            // Effective quota = plan's monthly quota + purchased extra-credit balance.
            var quota = (plan?.MonthlyMessageQuota ?? 0) + s.ExtraMessageCredits;
            var remaining = quota - s.MessagesUsedThisPeriod;
            var isLow = quota > 0 && remaining < quota * LowBalanceThreshold;

            return new BalanceRow(
                s.CompanyId,
                nameById.TryGetValue(s.CompanyId, out var n) ? n : "(unknown)",
                s.PlanId,
                plan?.Name ?? "(deleted plan)",
                quota,
                s.MessagesUsedThisPeriod,
                remaining,
                s.CurrentPeriodEnd,
                isLow
            );
        })
        .OrderByDescending(r => r.IsLow)
        .ThenBy(r => r.Remaining)
        .ToList();
    }

    // ── Dashboard KPIs ──────────────────────────────────────────────────────────

    public async Task<DashboardResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var cutoff30d = now.AddDays(-30);

        var totalCompanies = await db.Companies.CountAsync(ct);
        var activeCompanies = await db.Companies.CountAsync(c => c.IsActive, ct);

        var activeSubscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.IsActive && s.Status == SubscriptionStatus.Active, ct);

        // Count/revenue from the subscribe audit trail (includes STACK re-subscribes and uses
        // snapshotted prices), consistent with GetPackagesAsync.
        var monthPurchases = await db.SubscriptionPurchases
            .IgnoreQueryFilters()
            .Where(p => p.CreatedAt >= monthStart)
            .Select(p => p.Price)
            .ToListAsync(ct);
        var revenueThisMonth = monthPurchases.Sum();

        var newSignupsThisMonth = await db.Companies
            .CountAsync(c => c.CreatedAt >= monthStart, ct);

        var msgStats = await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound && m.CreatedAt >= cutoff30d)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sent = g.Count(),
                Billable = g.Count(m => m.Billable == true),
            })
            .FirstOrDefaultAsync(ct);

        var balances = await GetBalanceRowsAsync(ct);
        var lowBalance = balances.Count(b => b.IsLow);

        return new DashboardResponse(
            totalCompanies,
            activeCompanies,
            activeSubscriptions,
            monthPurchases.Count,
            revenueThisMonth,
            newSignupsThisMonth,
            msgStats?.Sent ?? 0,
            msgStats?.Billable ?? 0,
            lowBalance
        );
    }

    // ── Usage analytics ──────────────────────────────────────────────────────────

    public async Task<UsageReportResponse> GetUsageAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (rangeFrom, rangeTo) = NormalizeRange(from, to);

        var stats = await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound
                        && m.CreatedAt >= rangeFrom && m.CreatedAt <= rangeTo)
            .GroupBy(m => m.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Sent = g.Count(),
                Delivered = g.Count(m => m.Status == MessageStatus.Delivered),
                Read = g.Count(m => m.Status == MessageStatus.Read),
                Failed = g.Count(m => m.Status == MessageStatus.Failed),
                Billable = g.Count(m => m.Billable == true),
            })
            .ToListAsync(ct);

        var companyNames = await db.Companies
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var nameById = companyNames.ToDictionary(c => c.Id, c => c.Name);

        var rows = stats
            .Select(s => new CompanyUsageRow(
                s.CompanyId,
                nameById.TryGetValue(s.CompanyId, out var n) ? n : "(unknown)",
                s.Sent,
                s.Delivered,
                s.Read,
                s.Failed,
                s.Billable
            ))
            .OrderByDescending(r => r.Sent)
            .ToList();

        return new UsageReportResponse(
            rangeFrom,
            rangeTo,
            rows.Sum(r => r.Sent),
            rows.Sum(r => r.Billable),
            rows
        );
    }

    /// <summary>Defaults a missing range to the last 30 days; ensures from &lt;= to.</summary>
    private static (DateTime From, DateTime To) NormalizeRange(DateTime? from, DateTime? to)
    {
        var rangeTo = (to ?? DateTime.UtcNow).ToUniversalTime();
        var rangeFrom = (from ?? rangeTo.AddDays(-30)).ToUniversalTime();
        if (rangeFrom > rangeTo) (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        return (rangeFrom, rangeTo);
    }
}
