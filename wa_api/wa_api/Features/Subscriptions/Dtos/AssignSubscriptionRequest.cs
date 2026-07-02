using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/subscriptions/assign</c> (SuperAdmin only). Places a company
/// onto a plan. If the company already has a LIVE subscription (not expired and messages
/// remaining) the new plan's quota is stacked onto the running balance and the expiry is
/// extended from the current expiry; otherwise a fresh period starts from today (any leftover
/// on an expired/exhausted subscription is forfeited).
/// </summary>
public record AssignSubscriptionRequest(
    [Required] Guid CompanyId,
    [Required] Guid PlanId,
    [Range(1, 366)] int? PeriodDays
);
