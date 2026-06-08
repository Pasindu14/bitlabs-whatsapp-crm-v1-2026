using wa_api.Features.Plans.Dtos;

namespace wa_api.Features.Plans;

public interface IPlanService
{
    /// <summary>Paged, searchable list of plans (newest first by default).</summary>
    Task<(IReadOnlyList<PlanResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Loads a single plan by id. Throws if not found.</summary>
    Task<PlanResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a plan. Throws on duplicate name / unknown feature flag.</summary>
    Task<PlanResponse> CreateAsync(CreatePlanRequest request, CancellationToken ct = default);

    /// <summary>Updates editable fields. Throws on duplicate name / unknown feature flag.</summary>
    Task<PlanResponse> UpdateAsync(Guid id, UpdatePlanRequest request, CancellationToken ct = default);

    /// <summary>Marks a plan active (IsActive = true). Throws if not found.</summary>
    Task<PlanResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks a plan inactive (IsActive = false). Throws if not found.</summary>
    Task<PlanResponse> DeactivateAsync(Guid id, CancellationToken ct = default);
}
