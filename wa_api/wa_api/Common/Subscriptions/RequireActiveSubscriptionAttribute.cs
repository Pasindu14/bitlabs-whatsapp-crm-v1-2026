using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace wa_api.Common.Subscriptions;

/// <summary>
/// Endpoint guard: runs <see cref="ISubscriptionGate.EnsureCanSendAsync"/> before the action.
/// Composes per-endpoint like <c>[Authorize]</c>. A thrown gate exception is mapped to the
/// standard <c>ApiError</c> by <c>GlobalExceptionMiddleware</c> (SUBSCRIPTION_INACTIVE / QUOTA_EXCEEDED).
/// <para>
/// Used as a filter (not middleware) so it can target only the paid endpoints. This is the
/// PRD 3.1 stub; the full 3.4 gate widens coverage to campaign endpoints + central policy.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireActiveSubscriptionAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>
    /// When true, verify only an active, in-period subscription and SKIP the quota check — the endpoint
    /// decides per send whether a credit is consumed, and a free reply inside the open 24-hour
    /// customer-service window must still go through on an exhausted balance. Sends that DO consume a
    /// credit remain blocked by <see cref="ISubscriptionMeter"/>'s atomic ceiling, which throws the same
    /// QUOTA_EXCEEDED. Leave false on endpoints where every send is charged (campaigns).
    /// </summary>
    public bool SkipQuotaCheck { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<ISubscriptionGate>();
        var ct = context.HttpContext.RequestAborted;

        if (SkipQuotaCheck) await gate.EnsureActiveAsync(ct);
        else await gate.EnsureCanSendAsync(ct);

        await next();
    }
}
