using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// A subscription as returned by the assign/change/read endpoints. Flattens the plan's
/// quota + name so callers don't need a second fetch. <see cref="HasSubscription"/> is false
/// for the "no active subscription" shape returned by the tenant read endpoint.
/// </summary>
public record SubscriptionResponse(
    Guid? Id,
    Guid CompanyId,
    string? CompanyName,
    Guid? PlanId,
    string? PlanName,
    SubscriptionStatus? Status,
    int MonthlyMessageQuota,
    int ExtraMessageCredits,
    int EffectiveMessageQuota,
    int MessagesUsedThisPeriod,
    int MessagesRemaining,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool IsActive,
    bool HasSubscription,
    DateTime? CreatedAt
)
{
    /// <summary>The empty shape for a company with no active subscription.</summary>
    public static SubscriptionResponse None(Guid companyId, string? companyName = null)
        => new(null, companyId, companyName, null, null, null, 0, 0, 0, 0, 0, null, null, false, false, null);
}
