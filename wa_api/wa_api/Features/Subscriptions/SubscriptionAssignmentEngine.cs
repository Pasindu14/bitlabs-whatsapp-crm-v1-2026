using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Features.Subscriptions;

/// <summary>
/// Pure "stack or fresh" subscription assignment math (accumulate-until-expiry model), extracted
/// out of <see cref="SubscriptionService"/> so both the SuperAdmin manual-assign HTTP path
/// (<see cref="SubscriptionService.AssignAsync"/>) and the PayHere webhook job — which runs with
/// no tenant context and loads its rows via <c>IgnoreQueryFilters()</c> — share one implementation
/// instead of maintaining the stack-vs-fresh rule in two places. Operates only on already-loaded
/// entities; no DB or tenant-context dependency.
/// </summary>
public static class SubscriptionAssignmentEngine
{
    /// <summary>
    /// A subscription is "live" (and therefore stackable) when it has not expired AND still has
    /// messages remaining. If either is false the customer must start a fresh period.
    /// </summary>
    public static bool IsLive(Subscription sub, DateTime now)
    {
        var effective = (sub.Plan?.MonthlyMessageQuota ?? 0) + sub.ExtraMessageCredits;
        var remaining = effective - sub.MessagesUsedThisPeriod;
        return sub.CurrentPeriodEnd > now && remaining > 0;
    }

    /// <summary>
    /// STACK path: folds the new plan's quota onto a LIVE subscription in place and extends the
    /// expiry from the current expiry. Caller persists via <c>SaveChangesAsync</c>.
    /// </summary>
    public static void ApplyStack(Subscription existing, Plan plan, int periodDays)
    {
        existing.ExtraMessageCredits += plan.MonthlyMessageQuota;
        existing.CurrentPeriodEnd = existing.CurrentPeriodEnd.AddDays(periodDays);
    }

    /// <summary>
    /// FRESH path: builds a brand-new subscription row starting today. Caller must cancel any
    /// existing active row for the company FIRST (own SaveChanges) so the unique filtered index
    /// on (CompanyId WHERE Status='Active') never sees two active rows.
    /// </summary>
    public static Subscription BuildFresh(Guid companyId, Plan plan, DateTime now, int periodDays)
    {
        return new Subscription
        {
            CompanyId = companyId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(periodDays),
            MessagesUsedThisPeriod = 0,
            Plan = plan,   // nav set so BuildPurchase can read the plan's quota
        };
    }

    /// <summary>
    /// Builds an audit row for a single subscribe. <paramref name="sub"/> supplies the resulting
    /// balance/expiry (its <see cref="Subscription.Plan"/> nav must be loaded); <paramref name="subscribedPlan"/>
    /// is the plan chosen in this purchase (snapshotted for name/quota/price).
    /// </summary>
    public static SubscriptionPurchase BuildPurchase(
        Guid companyId, Subscription sub, Plan subscribedPlan,
        SubscriptionPurchaseMode mode, int periodDays)
    {
        var baseQuota = sub.Plan?.MonthlyMessageQuota ?? 0;
        var balanceAfter = Math.Max(0, baseQuota + sub.ExtraMessageCredits - sub.MessagesUsedThisPeriod);
        return new SubscriptionPurchase
        {
            CompanyId = companyId,          // explicit — caller may have no tenant context
            SubscriptionId = sub.Id,
            PlanId = subscribedPlan.Id,
            PlanName = subscribedPlan.Name,
            Mode = mode,
            MessagesAdded = subscribedPlan.MonthlyMessageQuota,
            PeriodDays = periodDays,
            BalanceAfter = balanceAfter,
            PeriodEndAfter = sub.CurrentPeriodEnd,
            Price = subscribedPlan.Price,
            Currency = subscribedPlan.Currency,
        };
    }
}
