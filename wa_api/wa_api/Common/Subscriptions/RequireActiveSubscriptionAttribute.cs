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
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<ISubscriptionGate>();
        await gate.EnsureCanSendAsync(context.HttpContext.RequestAborted);
        await next();
    }
}
