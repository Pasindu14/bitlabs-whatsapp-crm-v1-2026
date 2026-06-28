using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Common.Subscriptions;

/// <summary>
/// Default <see cref="ISubscriptionGate"/>. Reads the caller's active subscription through the
/// EF global query filter (scoped to the JWT company) and enforces active + in-quota.
/// </summary>
public class SubscriptionGate(AppDbContext db, ITenantContext tenant) : ISubscriptionGate
{
    public async Task EnsureCanSendAsync(CancellationToken ct = default)
    {
        // Platform plane: SuperAdmin isn't a tenant and isn't subject to plan quotas.
        if (tenant.IsSuperAdmin)
            return;

        if (tenant.CompanyId is null)
            throw new AuthorizationException("subscription");

        // Query filter restricts to the caller's company; Include the plan for its quota.
        var sub = await db.Subscriptions.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is null)
            throw new BusinessRuleException("SUBSCRIPTION_INACTIVE",
                "Your company has no active subscription. Contact your platform administrator.");

        if (sub.CurrentPeriodEnd < DateTime.UtcNow)
            throw new BusinessRuleException("SUBSCRIPTION_INACTIVE",
                "Your subscription period has ended. Contact your platform administrator.");

        if (sub.MessagesUsedThisPeriod >= sub.Plan.MonthlyMessageQuota + sub.ExtraMessageCredits)
            throw new BusinessRuleException("QUOTA_EXCEEDED",
                "Your monthly message quota has been reached.");
    }

    public async Task EnsureCanSendBatchAsync(int count, CancellationToken ct = default)
    {
        if (tenant.IsSuperAdmin)
            return;

        if (tenant.CompanyId is null)
            throw new AuthorizationException("subscription");

        var sub = await db.Subscriptions.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is null)
            throw new BusinessRuleException("SUBSCRIPTION_INACTIVE",
                "Your company has no active subscription. Contact your platform administrator.");

        if (sub.CurrentPeriodEnd < DateTime.UtcNow)
            throw new BusinessRuleException("SUBSCRIPTION_INACTIVE",
                "Your subscription period has ended. Contact your platform administrator.");

        var remaining = sub.Plan.MonthlyMessageQuota + sub.ExtraMessageCredits - sub.MessagesUsedThisPeriod;
        if (remaining < count)
            throw new BusinessRuleException("INSUFFICIENT_QUOTA",
                $"Insufficient message quota. This campaign requires {count} messages but only {Math.Max(0, remaining)} remain in your current period.");
    }
}
