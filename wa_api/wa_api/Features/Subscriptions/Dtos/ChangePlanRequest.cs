using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// Payload for <c>PUT /api/v1/subscriptions/company/{companyId}/plan</c> (SuperAdmin only).
/// Points the company's active subscription at a different plan, optionally restarting the period.
/// </summary>
public record ChangePlanRequest(
    [Required] Guid PlanId,
    bool? ResetPeriod,
    [Range(1, 366)] int? PeriodDays
);
