using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Common.Subscriptions;

/// <summary>
/// Answers "does this company's plan include feature X?" from <c>Plan.FeatureFlags</c>.
/// <para>
/// Plan flags were display-only until the chatbot arrived: a CompanyAdmin passes every
/// <c>RequirePermission</c> check by role, so nothing stopped a tenant from switching on a feature that
/// costs the platform money per use (every AI reply is a paid model call). This is the check that does.
/// </para>
/// <para>
/// Deliberately opt-in per endpoint (<see cref="RequirePlanFeatureAttribute"/>): the older flags
/// (templates, campaigns, contact lists, analytics) are NOT enforced, because existing customers were
/// sold plans whose flags were never checked and enforcing them now would lock people out.
/// </para>
/// </summary>
public interface IPlanFeatureGate
{
    /// <summary>
    /// True when the company's newest active, in-period subscription is on a plan carrying
    /// <paramref name="feature"/>. Works without a tenant context (webhook and job scopes).
    /// </summary>
    Task<bool> HasFeatureAsync(Guid companyId, string feature, CancellationToken ct = default);

    /// <summary>
    /// Throws <c>PLAN_FEATURE_REQUIRED</c> when the caller's company lacks <paramref name="feature"/>.
    /// SuperAdmin (platform plane) always passes.
    /// </summary>
    Task EnsureFeatureAsync(string feature, CancellationToken ct = default);
}

public sealed class PlanFeatureGate(AppDbContext db, ITenantContext tenant) : IPlanFeatureGate
{
    public async Task<bool> HasFeatureAsync(Guid companyId, string feature, CancellationToken ct = default)
    {
        // IgnoreQueryFilters + explicit company: this runs from webhook and Hangfire scopes too, where the
        // tenant query filter has no company and would return nothing. Same "newest active" rule as
        // SubscriptionGate and SubscriptionMeter, so all three agree on which subscription counts.
        var sub = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.CurrentPeriodEnd, s.Plan.FeatureFlags })
            .FirstOrDefaultAsync(ct);

        return sub is not null
            && sub.CurrentPeriodEnd >= DateTime.UtcNow
            && sub.FeatureFlags.Contains(feature);
    }

    public async Task EnsureFeatureAsync(string feature, CancellationToken ct = default)
    {
        if (tenant.IsSuperAdmin)
            return;

        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("plan feature");

        if (!await HasFeatureAsync(companyId, feature, ct))
            throw new BusinessRuleException("PLAN_FEATURE_REQUIRED",
                "Your current plan does not include this feature. Contact your platform administrator to upgrade.",
                new { feature });
    }
}

/// <summary>
/// Blocks an endpoint unless the caller's plan carries the named feature flag. Sits alongside
/// <c>[RequirePermission]</c>: that one asks "may this USER do it", this one asks "did the COMPANY buy it".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePlanFeatureAttribute(string feature) : Attribute, IAsyncActionFilter
{
    public string Feature { get; } = feature;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<IPlanFeatureGate>();
        await gate.EnsureFeatureAsync(Feature, context.HttpContext.RequestAborted);
        await next();
    }
}
