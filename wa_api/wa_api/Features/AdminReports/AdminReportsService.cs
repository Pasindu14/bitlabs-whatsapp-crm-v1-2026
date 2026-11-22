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
public class AdminReportsService(AppDbContext db, IConfiguration? configuration = null)
{
    /// <summary>Remaining quota below this fraction of the plan quota flags a low balance.</summary>
    private const double LowBalanceThreshold = 0.10;

    /// <summary>The AED peg. Costs are stored in USD; plan prices are AED.</summary>
    private const decimal DefaultUsdToAed = 3.6725m;

    private readonly decimal _usdToAed =
        configuration?.GetValue<decimal?>("Billing:UsdToAed") is decimal rate and > 0 ? rate : DefaultUsdToAed;

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
            // Floored at 0, matching SubscriptionService.Map — the two surfaces show the same number
            // for the same company, and an admin whose plan quota was edited below current usage never
            // sees a negative balance.
            var remaining = Math.Max(0, quota - s.MessagesUsedThisPeriod);
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
                // Exclude Failed so the dashboard "Sent" matches the quota-consuming count.
                Sent = g.Count(m => m.Status != MessageStatus.Failed),
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
                // "Sent" excludes Failed so this aligns with the quota counter (Failed is refunded).
                Sent = g.Count(m => m.Status != MessageStatus.Failed),
                Delivered = g.Count(m => m.Status == MessageStatus.Delivered),
                Read = g.Count(m => m.Status == MessageStatus.Read),
                Failed = g.Count(m => m.Status == MessageStatus.Failed),
                Billable = g.Count(m => m.Billable == true),
                MetaCostUsd = g.Sum(m => m.MetaCostUsd ?? 0m),
                UnpricedMeta = g.Count(m => m.Billable == true && m.MetaCostUsd == null),
            })
            .ToListAsync(ct);

        // Every Anthropic call, grouped by company and kind (plan 013).
        var ai = await db.AiReplyLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= rangeFrom && l.CreatedAt <= rangeTo)
            .GroupBy(l => new { l.CompanyId, l.Kind })
            .Select(g => new
            {
                g.Key.CompanyId,
                g.Key.Kind,
                Calls = g.Count(),
                CostUsd = g.Sum(l => l.CostUsd ?? 0m),
                Unpriced = g.Count(l => l.CostUsd == null && (l.InputTokens > 0 || l.OutputTokens > 0)),
                CacheRead = g.Sum(l => (long)l.CacheReadTokens),
                CacheWrite = g.Sum(l => (long)l.CacheWriteTokens),
            })
            .ToListAsync(ct);

        // The plan each company is on now, for the margin columns.
        var plans = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Select(s => new { s.CompanyId, s.Plan.Name, s.Plan.Price, s.Plan.Currency, s.CurrentPeriodEnd })
            .ToListAsync(ct);
        var planByCompany = plans
            .GroupBy(p => p.CompanyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CurrentPeriodEnd).First());

        var companyNames = await db.Companies
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var nameById = companyNames.ToDictionary(c => c.Id, c => c.Name);

        var statsByCompany = stats.ToDictionary(s => s.CompanyId);
        var aiByCompany = ai.GroupBy(a => a.CompanyId).ToDictionary(g => g.Key, g => g.ToList());
        var rangeDays = Math.Max(1m, (decimal)(rangeTo - rangeFrom).TotalDays);

        // A company that only spent on AI (previews, silent follow-ups) still belongs in a cost report.
        var rows = statsByCompany.Keys.Union(aiByCompany.Keys)
            .Select(companyId =>
            {
                statsByCompany.TryGetValue(companyId, out var s);
                var aiRows = aiByCompany.GetValueOrDefault(companyId) ?? [];

                var aiCost = aiRows.Sum(a => a.CostUsd);
                var metaCost = s?.MetaCostUsd ?? 0m;
                var totalUsd = aiCost + metaCost;
                var totalAed = Math.Round(totalUsd * _usdToAed, 2);
                var sent = s?.Sent ?? 0;

                var cacheRead = aiRows.Sum(a => a.CacheRead);
                var cacheWrite = aiRows.Sum(a => a.CacheWrite);

                planByCompany.TryGetValue(companyId, out var plan);
                decimal? planForRange = plan is null ? null
                    : string.Equals(plan.Currency, "AED", StringComparison.OrdinalIgnoreCase)
                        ? Math.Round(plan.Price * rangeDays / 30m, 2)
                    : string.Equals(plan.Currency, "USD", StringComparison.OrdinalIgnoreCase)
                        ? Math.Round(plan.Price * _usdToAed * rangeDays / 30m, 2)
                    : null;

                var cost = new UsageCost(
                    AiCostUsd: Math.Round(aiCost, 6),
                    AiByKind: [.. aiRows
                        .OrderBy(a => a.Kind)
                        .Select(a => new AiCostByKind(a.Kind.ToString(), a.Calls, Math.Round(a.CostUsd, 6)))],
                    UnpricedAiCalls: aiRows.Sum(a => a.Unpriced),
                    CacheHitRate: cacheRead + cacheWrite == 0 ? null : (double)cacheRead / (cacheRead + cacheWrite),
                    MetaCostUsd: Math.Round(metaCost, 6),
                    UnpricedMetaMessages: s?.UnpricedMeta ?? 0,
                    TotalCostUsd: Math.Round(totalUsd, 6),
                    TotalCostAed: totalAed,
                    CostPerMessageAed: sent == 0 ? null : Math.Round(totalUsd * _usdToAed / sent, 4),
                    PlanName: plan?.Name,
                    PlanPriceForRangeAed: planForRange,
                    MarginAed: planForRange is { } p ? p - totalAed : null,
                    CostShareOfPlan: planForRange is > 0 ? (double)(totalAed / planForRange.Value) : null);

                return new CompanyUsageRow(
                    companyId,
                    nameById.TryGetValue(companyId, out var n) ? n : "(unknown)",
                    sent,
                    s?.Delivered ?? 0,
                    s?.Read ?? 0,
                    s?.Failed ?? 0,
                    s?.Billable ?? 0,
                    cost);
            })
            .OrderByDescending(r => r.Sent)
            .ThenByDescending(r => r.Cost?.TotalCostUsd ?? 0)
            .ToList();

        var totalAi = rows.Sum(r => r.Cost?.AiCostUsd ?? 0m);
        var totalMeta = rows.Sum(r => r.Cost?.MetaCostUsd ?? 0m);

        return new UsageReportResponse(
            rangeFrom,
            rangeTo,
            rows.Sum(r => r.Sent),
            rows.Sum(r => r.Billable),
            rows,
            totalAi,
            totalMeta,
            Math.Round((totalAi + totalMeta) * _usdToAed, 2),
            _usdToAed
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
