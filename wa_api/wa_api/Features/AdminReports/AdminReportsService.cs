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

        // Every subscription created in the window = one package purchased. Counts all rows
        // regardless of current status (a later plan change soft-deletes the old row, but the
        // purchase still happened).
        var purchases = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.CreatedAt >= rangeFrom && s.CreatedAt <= rangeTo)
            .Select(s => new { s.PlanId, s.CreatedAt })
            .ToListAsync(ct);

        var plans = await db.Plans
            .Select(p => new { p.Id, p.Name, p.Price, p.Currency })
            .ToListAsync(ct);
        var planById = plans.ToDictionary(p => p.Id);

        var byPlan = purchases
            .GroupBy(p => p.PlanId)
            .Select(g =>
            {
                planById.TryGetValue(g.Key, out var plan);
                var price = plan?.Price ?? 0m;
                var count = g.Count();
                return new PlanPackageRow(
                    g.Key,
                    plan?.Name ?? "(deleted plan)",
                    price,
                    plan?.Currency ?? "USD",
                    count,
                    price * count
                );
            })
            .OrderByDescending(r => r.Revenue)
            .ToList();

        var byDay = purchases
            .GroupBy(p => p.CreatedAt.Date)
            .Select(g => new DailyPackageRow(
                g.Key,
                g.Count(),
                g.Sum(x => planById.TryGetValue(x.PlanId, out var pl) ? pl.Price : 0m)
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

        var monthPurchases = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.CreatedAt >= monthStart)
            .Select(s => s.PlanId)
            .ToListAsync(ct);

        var planPrices = await db.Plans
            .Select(p => new { p.Id, p.Price })
            .ToListAsync(ct);
        var priceById = planPrices.ToDictionary(p => p.Id, p => p.Price);
        var revenueThisMonth = monthPurchases
            .Sum(planId => priceById.TryGetValue(planId, out var pr) ? pr : 0m);

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
