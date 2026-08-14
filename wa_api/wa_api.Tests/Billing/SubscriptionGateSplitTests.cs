using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Common.Tenancy;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins the gate split introduced with free-window billing. <see cref="ISubscriptionGate.EnsureActiveAsync"/>
/// checks only that a subscription exists and its period hasn't ended; the quota check stays behind
/// <see cref="ISubscriptionGate.EnsureCanSendAsync"/>. That difference is what lets an agent keep replying
/// inside the free 24-hour window on an exhausted balance — so it needs pinning, not just reading: the
/// failure mode of getting it wrong is an open hole (any endpoint ungated) rather than a visible bug.
/// </summary>
public class SubscriptionGateSplitTests
{
    private sealed class Tenant(Guid? companyId, bool isSuperAdmin = false) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin { get; } = isSuperAdmin;
    }

    private static AppDbContext NewDb(ITenantContext tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"gate-split-{Guid.NewGuid()}")
                .Options,
            tenant);

    private static Plan NewPlan(int quota) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Plan",
        MonthlyMessageQuota = quota,
        Price = 10m,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static Subscription NewSub(Guid companyId, Guid planId, int used, DateTime periodEnd) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PlanId = planId,
        Status = SubscriptionStatus.Active,
        CurrentPeriodStart = DateTime.UtcNow.AddDays(-1),
        CurrentPeriodEnd = periodEnd,
        MessagesUsedThisPeriod = used,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    /// <summary>Seeds a live subscription with the given consumption and returns a gate over it.</summary>
    private static async Task<(AppDbContext Db, SubscriptionGate Gate)> SeedAsync(
        int quota, int used, DateTime? periodEnd = null)
    {
        var companyId = Guid.NewGuid();
        var tenant = new Tenant(companyId);
        var db = NewDb(tenant);
        var plan = NewPlan(quota);
        db.Plans.Add(plan);
        db.Subscriptions.Add(NewSub(companyId, plan.Id, used, periodEnd ?? DateTime.UtcNow.AddDays(30)));
        await db.SaveChangesAsync();
        return (db, new SubscriptionGate(db, tenant));
    }

    [Fact] // The point of the split: an exhausted balance blocks charged sends but not free ones.
    public async Task ExhaustedQuota_BlocksCanSend_ButNotActive()
    {
        var (db, gate) = await SeedAsync(quota: 100, used: 100);
        await using var _ = db;

        await gate.EnsureActiveAsync(); // must not throw — free in-window replies still go through

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => gate.EnsureCanSendAsync());
        Assert.Equal("QUOTA_EXCEEDED", ex.ErrorCode);
    }

    [Fact] // Guards against over-relaxing: an expired subscription must still block everything.
    public async Task ExpiredPeriod_BlocksBoth()
    {
        var (db, gate) = await SeedAsync(quota: 100, used: 0, periodEnd: DateTime.UtcNow.AddDays(-1));
        await using var _ = db;

        var active = await Assert.ThrowsAsync<BusinessRuleException>(() => gate.EnsureActiveAsync());
        var canSend = await Assert.ThrowsAsync<BusinessRuleException>(() => gate.EnsureCanSendAsync());
        Assert.Equal("SUBSCRIPTION_INACTIVE", active.ErrorCode);
        Assert.Equal("SUBSCRIPTION_INACTIVE", canSend.ErrorCode);
    }

    [Fact] // No subscription at all is not a "free window" situation — both paths refuse.
    public async Task NoActiveSubscription_BlocksBoth()
    {
        var tenant = new Tenant(Guid.NewGuid());
        await using var db = NewDb(tenant);
        var gate = new SubscriptionGate(db, tenant);

        var active = await Assert.ThrowsAsync<BusinessRuleException>(() => gate.EnsureActiveAsync());
        var canSend = await Assert.ThrowsAsync<BusinessRuleException>(() => gate.EnsureCanSendAsync());
        Assert.Equal("SUBSCRIPTION_INACTIVE", active.ErrorCode);
        Assert.Equal("SUBSCRIPTION_INACTIVE", canSend.ErrorCode);
    }

    [Fact] // Extra credits count toward the ceiling on both paths.
    public async Task ExtraCredits_LiftTheCeiling()
    {
        var companyId = Guid.NewGuid();
        var tenant = new Tenant(companyId);
        await using var db = NewDb(tenant);
        var plan = NewPlan(quota: 100);
        db.Plans.Add(plan);
        var sub = NewSub(companyId, plan.Id, used: 100, DateTime.UtcNow.AddDays(30));
        sub.ExtraMessageCredits = 50; // topped up — 150 total, 100 used
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var gate = new SubscriptionGate(db, tenant);
        await gate.EnsureActiveAsync();
        await gate.EnsureCanSendAsync(); // neither throws — 50 credits remain
    }

    [Fact] // SuperAdmin isn't a tenant and bypasses both checks, including the new one.
    public async Task SuperAdmin_BypassesBoth()
    {
        var tenant = new Tenant(companyId: null, isSuperAdmin: true);
        await using var db = NewDb(tenant);
        var gate = new SubscriptionGate(db, tenant);

        await gate.EnsureActiveAsync();
        await gate.EnsureCanSendAsync();
    }
}
