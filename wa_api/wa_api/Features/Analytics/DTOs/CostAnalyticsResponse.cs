namespace wa_api.Features.Analytics.DTOs;

public record BillableCategoryCount(string Category, bool Billable, int Count);

public record CostAnalyticsResponse(
    int TotalBillable,
    int TotalNonBillable,
    IReadOnlyList<BillableCategoryCount> Breakdown
);
