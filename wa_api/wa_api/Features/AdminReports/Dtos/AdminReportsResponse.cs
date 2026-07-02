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

/// <summary>Message volume for one company over the selected range.</summary>
public record CompanyUsageRow(
    Guid CompanyId,
    string CompanyName,
    int Sent,
    int Delivered,
    int Read,
    int Failed,
    int Billable
);

public record UsageReportResponse(
    DateTime From,
    DateTime To,
    int TotalSent,
    int TotalBillable,
    IReadOnlyList<CompanyUsageRow> Companies
);
