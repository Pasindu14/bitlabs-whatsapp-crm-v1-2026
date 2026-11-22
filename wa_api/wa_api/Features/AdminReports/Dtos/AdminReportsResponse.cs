namespace wa_api.Features.AdminReports.Dtos;

// ── Packages purchased (per date range) ───────────────────────────────────────

/// <summary>One plan's slice of the packages-purchased report.</summary>
public record PlanPackageRow(
    Guid PlanId,
    string PlanName,
    decimal Price,
    string Currency,
    int Count,
    decimal Revenue
);

/// <summary>One day's slice of the packages-purchased report (for charting).</summary>
public record DailyPackageRow(
    DateTime Date,
    int Count,
    decimal Revenue
);

/// <summary>
/// Packages (subscribes) purchased in a date range. Sourced from the
/// <c>SubscriptionPurchase</c> audit trail — one row per subscribe including STACK
/// re-subscribes — with price/currency snapshotted at purchase time.
/// </summary>
public record PackagesReportResponse(
    DateTime From,
    DateTime To,
    int TotalPackages,
    decimal TotalRevenue,
    IReadOnlyList<PlanPackageRow> ByPlan,
    IReadOnlyList<DailyPackageRow> ByDay
);

// ── Balances ──────────────────────────────────────────────────────────────────

/// <summary>Remaining message quota for one company's active subscription.</summary>
public record BalanceRow(
    Guid CompanyId,
    string CompanyName,
    Guid PlanId,
    string PlanName,
    int MonthlyQuota,
    int Used,
    int Remaining,
    DateTime PeriodEnd,
    bool IsLow
);

public record BalancesResponse(IReadOnlyList<BalanceRow> Companies);

// ── Dashboard KPIs ────────────────────────────────────────────────────────────

/// <summary>Headline platform numbers for the SuperAdmin reports dashboard.</summary>
public record DashboardResponse(
    int TotalCompanies,
    int ActiveCompanies,
    int ActiveSubscriptions,
    int PackagesSoldThisMonth,
    decimal RevenueThisMonth,
    int NewSignupsThisMonth,
    int MessagesSent30d,
    int BillableMessages30d,
    int CompaniesLowBalance
);

// ── Usage analytics (per company, per date range) ─────────────────────────────

/// <summary>
/// Message volume and what it cost us (plan 013) for one company over the selected range. Costs are USD
/// as stored; the AED figures are converted with <c>Billing:UsdToAed</c> for display next to plan prices.
/// </summary>
public record CompanyUsageRow(
    Guid CompanyId,
    string CompanyName,
    int Sent,
    int Delivered,
    int Read,
    int Failed,
    int Billable,
    UsageCost? Cost = null
);

/// <summary>What one company cost us over the range, and how that compares to what it pays.</summary>
public record UsageCost(
    /// <summary>All Anthropic spend, previews included — the bill is the bill.</summary>
    decimal AiCostUsd,
    IReadOnlyList<AiCostByKind> AiByKind,
    /// <summary>Calls whose model had no configured price. Non-zero means AiCostUsd is understated.</summary>
    int UnpricedAiCalls,
    /// <summary>Cached-prefix reads ÷ (reads + writes). Low means the 5-minute cache keeps expiring.</summary>
    double? CacheHitRate,
    decimal MetaCostUsd,
    /// <summary>Billable messages Meta has not reported a price for yet (or sent before pricing was tracked).</summary>
    int UnpricedMetaMessages,
    decimal TotalCostUsd,
    decimal TotalCostAed,
    /// <summary>Total cost ÷ sent messages. Null when nothing was sent.</summary>
    decimal? CostPerMessageAed,
    string? PlanName,
    /// <summary>The active plan's monthly price, pro-rated to the range length (days ÷ 30). AED only.</summary>
    decimal? PlanPriceForRangeAed,
    /// <summary>PlanPriceForRangeAed − TotalCostAed.</summary>
    decimal? MarginAed,
    /// <summary>TotalCostAed ÷ PlanPriceForRangeAed. Above 0.5 is flagged in the UI.</summary>
    double? CostShareOfPlan
);

public record AiCostByKind(string Kind, int Calls, decimal CostUsd);

public record UsageReportResponse(
    DateTime From,
    DateTime To,
    int TotalSent,
    int TotalBillable,
    IReadOnlyList<CompanyUsageRow> Companies,
    decimal TotalAiCostUsd = 0,
    decimal TotalMetaCostUsd = 0,
    decimal TotalCostAed = 0,
    decimal UsdToAed = 0
);
