namespace wa_api.Features.Plans.Dtos;

/// <summary>A plan tier as returned by the list/detail/create/update endpoints.</summary>
public record PlanResponse(
    Guid Id,
    string Name,
    int MonthlyMessageQuota,
    decimal Price,
    string Currency,
    IReadOnlyList<string> FeatureFlags,
    bool IsOnline,
    bool IsActive,
    DateTime CreatedAt
);
