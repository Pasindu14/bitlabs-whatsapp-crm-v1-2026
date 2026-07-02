using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions;
using wa_api.Features.Subscriptions.Dtos;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Subscriptions;

/// <summary>
/// Unit tests for the "stack or fresh" re-subscribe rule in <see cref="SubscriptionService.AssignAsync"/>.
/// STACK when the current subscription is live (not expired AND messages remaining): the new plan's
/// quota is added to the running balance and the expiry extends from the CURRENT expiry. FRESH
/// otherwise (no sub / expired / exhausted): leftover is forfeited and a new period starts today.
/// Backed by EF Core InMemory with a SuperAdmin tenant context (assign is a SuperAdmin operation).
/// </summary>
public class SubscriptionServiceAssignTests
{
    private sealed class SuperAdminTenant : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => true;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"sub-assign-{Guid.NewGuid()}")
                .Options,
            new SuperAdminTenant());

    private static Company Company() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Acme",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Plan Plan(int quota) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Plan-{quota}",
        MonthlyMessageQuota = quota,
        Price = 10m,
        Currency = "USD",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Subscription Existing(Guid companyId, Guid planId, int used, DateTime periodEnd) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PlanId = planId,
        Status = SubscriptionStatus.Active,
        MessagesUsedThisPeriod = used,
        ExtraMessageCredits = 0,
        CurrentPeriodStart = DateTime.UtcNow.AddDays(-5),
        CurrentPeriodEnd = periodEnd,
        CreatedAt = DateTime.UtcNow.AddDays(-5),
        UpdatedAt = DateTime.UtcNow.AddDays(-5),
        IsActive = true,
    };

    [Fact]
    public async Task NewCustomer_StartsFreshFromToday()
    {
        await using var db = NewDb();
        var company = Company();
        var plan = Plan(10_000);
        db.Companies.Add(company);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        var svc = new SubscriptionService(db, new SuperAdminTenant());
        var before = DateTime.UtcNow;
        var res = await svc.AssignAsync(new AssignSubscriptionRequest(company.Id, plan.Id, 30));

        Assert.Equal(10_000, res.MessagesRemaining);
        Assert.Equal(10_000, res.EffectiveMessageQuota);
        Assert.Equal(0, res.MessagesUsedThisPeriod);
        // Expiry ≈ today + 30 days.
        Assert.InRange(res.CurrentPeriodEnd!.Value, before.AddDays(30).AddSeconds(-5), DateTime.UtcNow.AddDays(30).AddSeconds(5));
    }

    [Fact]
    public async Task Live_Stacks_AddsBalance_AndExtendsFromCurrentExpiry()
    {
        await using var db = NewDb();
        var company = Company();
        var planA = Plan(10_000);
        var planB = Plan(10_000);
        var oldEnd = DateTime.UtcNow.AddDays(13);        // not expired
        db.Companies.Add(company);
        db.Plans.AddRange(planA, planB);
        db.Subscriptions.Add(Existing(company.Id, planA.Id, used: 4_000, periodEnd: oldEnd)); // 6,000 left → live
        await db.SaveChangesAsync();

        var svc = new SubscriptionService(db, new SuperAdminTenant());
        var res = await svc.AssignAsync(new AssignSubscriptionRequest(company.Id, planB.Id, 30));

        // 6,000 remaining + 10,000 new = 16,000; used unchanged.
        Assert.Equal(16_000, res.MessagesRemaining);
        Assert.Equal(20_000, res.EffectiveMessageQuota);
        Assert.Equal(4_000, res.MessagesUsedThisPeriod);
        // Expiry extends from the OLD expiry, not today.
        Assert.Equal(oldEnd.AddDays(30), res.CurrentPeriodEnd!.Value, TimeSpan.FromSeconds(2));

        // Still exactly one active subscription (stacked in place, not a new row).
        var activeCount = await db.Subscriptions.CountAsync(s => s.CompanyId == company.Id && s.Status == SubscriptionStatus.Active);
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task Expired_StartsFresh_ForfeitsLeftover_AndCancelsOld()
    {
        await using var db = NewDb();
        var company = Company();
        var planA = Plan(10_000);
        var planB = Plan(10_000);
        db.Companies.Add(company);
        db.Plans.AddRange(planA, planB);
        db.Subscriptions.Add(Existing(company.Id, planA.Id, used: 3_000, periodEnd: DateTime.UtcNow.AddDays(-2))); // expired, 7,000 left
        await db.SaveChangesAsync();

        var svc = new SubscriptionService(db, new SuperAdminTenant());
        var before = DateTime.UtcNow;
        var res = await svc.AssignAsync(new AssignSubscriptionRequest(company.Id, planB.Id, 30));

        // Leftover 7,000 forfeited — fresh 10,000 only.
        Assert.Equal(10_000, res.MessagesRemaining);
        Assert.Equal(10_000, res.EffectiveMessageQuota);
        Assert.InRange(res.CurrentPeriodEnd!.Value, before.AddDays(30).AddSeconds(-5), DateTime.UtcNow.AddDays(30).AddSeconds(5));

        // Old sub cancelled, exactly one active.
        var active = await db.Subscriptions.CountAsync(s => s.CompanyId == company.Id && s.Status == SubscriptionStatus.Active);
        var cancelled = await db.Subscriptions.CountAsync(s => s.CompanyId == company.Id && s.Status == SubscriptionStatus.Cancelled);
        Assert.Equal(1, active);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task Exhausted_ButNotExpired_StartsFresh()
    {
        await using var db = NewDb();
        var company = Company();
        var planA = Plan(10_000);
        var planB = Plan(5_000);
        db.Companies.Add(company);
        db.Plans.AddRange(planA, planB);
        db.Subscriptions.Add(Existing(company.Id, planA.Id, used: 10_000, periodEnd: DateTime.UtcNow.AddDays(10))); // 0 left, not expired
        await db.SaveChangesAsync();

        var svc = new SubscriptionService(db, new SuperAdminTenant());
        var before = DateTime.UtcNow;
        var res = await svc.AssignAsync(new AssignSubscriptionRequest(company.Id, planB.Id, 30));

        // Balance was 0 → fresh start from the new plan (no carry-over), expiry from today.
        Assert.Equal(5_000, res.MessagesRemaining);
        Assert.Equal(5_000, res.EffectiveMessageQuota);
        Assert.Equal(0, res.MessagesUsedThisPeriod);
        Assert.InRange(res.CurrentPeriodEnd!.Value, before.AddDays(30).AddSeconds(-5), DateTime.UtcNow.AddDays(30).AddSeconds(5));
    }
}
