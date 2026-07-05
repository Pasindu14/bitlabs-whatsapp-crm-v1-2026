using Microsoft.EntityFrameworkCore;
using wa_api.Common.Subscriptions;
using wa_api.Common.Tenancy;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins the atomic quota ceiling (C1). The meter is the enforcement point: TryReserveAsync only increments
/// when the send stays within quota+credits, so a send can never overshoot the plan; RefundAsync gives a
/// reservation back (floored at 0) when the send fails. (Concurrency is enforced in prod by the single
/// conditional SQL UPDATE; these tests exercise the InMemory ceiling logic.)
/// </summary>
public class SubscriptionMeterTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"meter-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static async Task<(AppDbContext db, Guid companyId, Guid subId)> SeedAsync(
        int quota, int used, int extraCredits = 0)
    {
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var db = NewDb(companyId);

        db.Plans.Add(new Plan { Id = planId, Name = "Test", MonthlyMessageQuota = quota });
        db.Subscriptions.Add(new Subscription
        {
            Id = subId,
            CompanyId = companyId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            MessagesUsedThisPeriod = used,
            ExtraMessageCredits = extraCredits,
            CurrentPeriodStart = DateTime.UtcNow.AddDays(-1),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(29),
        });
        await db.SaveChangesAsync();
        return (db, companyId, subId);
    }

    private static async Task<int> UsedAsync(AppDbContext db, Guid subId) =>
        (await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == subId))
            .MessagesUsedThisPeriod;

    [Fact]
    public async Task Reserve_UnderCeiling_Succeeds()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 998);
        var meter = new SubscriptionMeter(db);

        var reserved = await meter.TryReserveAsync(companyId, 1);

        Assert.Equal(subId, reserved);
        Assert.Equal(999, await UsedAsync(db, subId));
    }

    [Fact]
    public async Task Reserve_AtCeiling_IsRejected_AndCounterUnchanged()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 1000);
        var meter = new SubscriptionMeter(db);

        var reserved = await meter.TryReserveAsync(companyId, 1);

        Assert.Null(reserved);                              // ceiling — no overshoot
        Assert.Equal(1000, await UsedAsync(db, subId));     // counter untouched
    }

    [Fact]
    public async Task Reserve_LastSlot_Succeeds_ThenNextIsRejected()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 999);
        var meter = new SubscriptionMeter(db);

        Assert.Equal(subId, await meter.TryReserveAsync(companyId, 1));   // 999 -> 1000
        Assert.Null(await meter.TryReserveAsync(companyId, 1));           // 1000 -> rejected
        Assert.Equal(1000, await UsedAsync(db, subId));
    }

    [Fact]
    public async Task Reserve_CountsExtraCreditsInTheCeiling()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 1000, extraCredits: 5);
        var meter = new SubscriptionMeter(db);

        Assert.Equal(subId, await meter.TryReserveAsync(companyId, 1));   // 1000 -> 1001, within 1005
        Assert.Equal(1001, await UsedAsync(db, subId));
    }

    [Fact]
    public async Task Reserve_NoActiveSubscription_ReturnsNull()
    {
        var db = NewDb(Guid.NewGuid());
        var meter = new SubscriptionMeter(db);

        Assert.Null(await meter.TryReserveAsync(Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task Refund_GivesBackReservation_FlooredAtZero()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 1000);
        var meter = new SubscriptionMeter(db);

        await meter.RefundAsync(subId, 1);
        Assert.Equal(999, await UsedAsync(db, subId));

        await meter.RefundAsync(subId, 10_000);            // can't go negative
        Assert.Equal(0, await UsedAsync(db, subId));
    }

    [Fact]
    public async Task ReserveThenRefund_NetsToZero_AndReopensACappedSlot()
    {
        var (db, companyId, subId) = await SeedAsync(quota: 1000, used: 1000);
        var meter = new SubscriptionMeter(db);

        Assert.Null(await meter.TryReserveAsync(companyId, 1));   // full
        await meter.RefundAsync(subId, 1);                       // 1000 -> 999
        Assert.Equal(subId, await meter.TryReserveAsync(companyId, 1));  // slot reopened
        Assert.Equal(1000, await UsedAsync(db, subId));
    }
}
