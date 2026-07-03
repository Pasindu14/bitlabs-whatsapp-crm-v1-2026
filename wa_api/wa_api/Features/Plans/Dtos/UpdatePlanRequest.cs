using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Plans.Dtos;

/// <summary>Payload for <c>PUT /api/v1/plans/{id}</c> (SuperAdmin only).</summary>
public record UpdatePlanRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Range(0, int.MaxValue)] int MonthlyMessageQuota,
    [Range(0, 1_000_000)] decimal Price,
    [StringLength(3, MinimumLength = 3)] string? Currency,
    IReadOnlyList<string>? FeatureFlags,
    bool IsOnline = false
);
