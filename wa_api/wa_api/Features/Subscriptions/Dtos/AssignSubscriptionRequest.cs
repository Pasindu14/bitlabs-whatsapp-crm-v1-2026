using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/subscriptions/assign</c> (SuperAdmin only). Places a company
/// onto a plan. Any existing active subscription for that company is cancelled first.
/// </summary>
public record AssignSubscriptionRequest(
    [Required] Guid CompanyId,
    [Required] Guid PlanId,
    [Range(1, 366)] int? PeriodDays
);
