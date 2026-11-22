using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.AdminReports;
using wa_api.Features.Companies;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.AdminReports;

/// <summary>
/// Unit tests for the SuperAdmin reporting aggregations. Backed by EF Core InMemory; the
/// service uses IgnoreQueryFilters() so a SuperAdmin tenant context is supplied for safety.
/// </summary>
public class AdminReportsServiceTests
{
    private sealed class SuperAdminTenant : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => true;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"admin-reports-{Guid.NewGuid()}")
                .Options,
            new SuperAdminTenant());

    private static Company Company(string name, bool active = true, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsActive = active,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = createdAt ?? DateTime.UtcNow,
    };

    private static Plan Plan(string name, int quota, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MonthlyMessageQuota = quota,
        Price = price,
        Currency = "USD",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Subscription Sub(
        Guid companyId, Guid planId, DateTime createdAt,
        int used = 0, SubscriptionStatus status = SubscriptionStatus.Active,
        DateTime? periodEnd = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PlanId = planId,
        Status = status,
        MessagesUsedThisPeriod = used,
        CurrentPeriodStart = createdAt,
        CurrentPeriodEnd = periodEnd ?? createdAt.AddDays(30),
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        IsActive = true,
    };

    private static SubscriptionPurchase Purchase(
        Guid companyId, Plan plan, DateTime createdAt,
        SubscriptionPurchaseMode mode = SubscriptionPurchaseMode.Fresh) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        SubscriptionId = Guid.NewGuid(),
        PlanId = plan.Id,
        PlanName = plan.Name,
        Mode = mode,
        MessagesAdded = plan.MonthlyMessageQuota,
        PeriodDays = 30,
        BalanceAfter = plan.MonthlyMessageQuota,
        PeriodEndAfter = createdAt.AddDays(30),
        Price = plan.Price,
        Currency = plan.Currency,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        IsActive = true,
    };

    private static Message Msg(
        Guid companyId, DateTime createdAt,
        MessageStatus status = MessageStatus.Sent, bool? billable = null,
        MessageDirection direction = MessageDirection.Outbound) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ContactId = Guid.NewGuid(),
        WabaConnectionId = Guid.NewGuid(),
        Direction = direction,
        Status = status,
        Billable = billable,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    [Fact]
    public async Task Packages_CountsAndRevenue_GroupedByPlan_WithinRange()
    {
        await using var db = NewDb();
        var co = Company("Acme");
        var planA = Plan("Starter", 1000, 10m);
        var planB = Plan("Pro", 5000, 25m);
        db.AddRange(co, planA, planB);

        var inRange = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var outOfRange = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(
            Purchase(co.Id, planA, inRange),
            Purchase(co.Id, planA, inRange),
            // A STACK re-subscribe mutates the existing Subscription row but still records a
            // purchase — must be counted (the old Subscriptions-based report missed these).
            Purchase(co.Id, planA, inRange, SubscriptionPurchaseMode.Stack),
            Purchase(co.Id, planB, inRange),
            Purchase(co.Id, planB, inRange),
            Purchase(co.Id, planA, outOfRange) // excluded by range
        );
        await db.SaveChangesAsync();

        var svc = new AdminReportsService(db);
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var result = await svc.GetPackagesAsync(from, to);

        Assert.Equal(5, result.TotalPackages);
        Assert.Equal(80m, result.TotalRevenue); // 3*10 + 2*25

        var rowA = Assert.Single(result.ByPlan, r => r.PlanName == "Starter");
        Assert.Equal(3, rowA.Count);
        Assert.Equal(30m, rowA.Revenue);

        var rowB = Assert.Single(result.ByPlan, r => r.PlanName == "Pro");
        Assert.Equal(2, rowB.Count);
        Assert.Equal(50m, rowB.Revenue);

        var day = Assert.Single(result.ByDay);
        Assert.Equal(5, day.Count);
        Assert.Equal(80m, day.Revenue);
    }

    [Fact]
    public async Task Balances_ComputesRemaining_AndFlagsLow()
    {
        await using var db = NewDb();
        var low = Company("LowCo");
        var healthy = Company("HealthyCo");
        var plan = Plan("Starter", 1000, 10m);
        db.AddRange(low, healthy, plan);
        db.AddRange(
            Sub(low.Id, plan.Id, DateTime.UtcNow, used: 950),     // remaining 50 < 100 → low
            Sub(healthy.Id, plan.Id, DateTime.UtcNow, used: 100)  // remaining 900 → ok
        );
        await db.SaveChangesAsync();

        var svc = new AdminReportsService(db);
        var result = await svc.GetBalancesAsync();

        Assert.Equal(2, result.Companies.Count);

        var lowRow = Assert.Single(result.Companies, r => r.CompanyName == "LowCo");
        Assert.Equal(50, lowRow.Remaining);
        Assert.True(lowRow.IsLow);

        var okRow = Assert.Single(result.Companies, r => r.CompanyName == "HealthyCo");
        Assert.Equal(900, okRow.Remaining);
        Assert.False(okRow.IsLow);

        // Low balances are sorted first.
        Assert.Equal("LowCo", result.Companies[0].CompanyName);
    }

    [Fact]
    public async Task Balances_IgnoresNonActiveSubscriptions()
    {
        await using var db = NewDb();
        var co = Company("Acme");
        var plan = Plan("Starter", 1000, 10m);
        db.AddRange(co, plan);
        db.Add(Sub(co.Id, plan.Id, DateTime.UtcNow, used: 10, status: SubscriptionStatus.Cancelled));
        await db.SaveChangesAsync();

        var svc = new AdminReportsService(db);
        var result = await svc.GetBalancesAsync();

        Assert.Empty(result.Companies);
    }

    [Fact]
    public async Task Dashboard_AggregatesPlatformKpis()
    {
        await using var db = NewDb();
        var now = DateTime.UtcNow;
        var c1 = Company("Acme", active: true, createdAt: now);
        var c2 = Company("Beta", active: true, createdAt: now);
        var c3 = Company("Gone", active: false, createdAt: now);
        var plan = Plan("Starter", 1000, 10m);
        db.AddRange(c1, c2, c3, plan);

        db.AddRange(
            Sub(c1.Id, plan.Id, now, used: 950),   // active + low balance
            Sub(c2.Id, plan.Id, now, used: 100)    // active
        );

        // Purchases drive PackagesSoldThisMonth / RevenueThisMonth (subscribe audit trail).
        db.AddRange(
            Purchase(c1.Id, plan, now),
            Purchase(c2.Id, plan, now)
        );

        db.AddRange(
            Msg(c1.Id, now, MessageStatus.Sent, billable: true),
            Msg(c1.Id, now, MessageStatus.Delivered, billable: true),
            Msg(c2.Id, now, MessageStatus.Failed, billable: false),
            Msg(c1.Id, now.AddDays(-40), MessageStatus.Sent, billable: true) // outside 30d window
        );
        await db.SaveChangesAsync();

        var svc = new AdminReportsService(db);
        var result = await svc.GetDashboardAsync();

        Assert.Equal(3, result.TotalCompanies);
        Assert.Equal(2, result.ActiveCompanies);
        Assert.Equal(2, result.ActiveSubscriptions);
        Assert.Equal(2, result.PackagesSoldThisMonth);
        Assert.Equal(20m, result.RevenueThisMonth);
        Assert.Equal(3, result.NewSignupsThisMonth);
        Assert.Equal(2, result.MessagesSent30d);     // Sent + Delivered; Failed excluded, 40-day-old excluded
        Assert.Equal(2, result.BillableMessages30d);
        Assert.Equal(1, result.CompaniesLowBalance);
    }

    [Fact]
    public async Task Usage_GroupsByCompany_AndRespectsRange()
    {
        await using var db = NewDb();
        var co = Company("Acme");
        db.Add(co);

        var inRange = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var outOfRange = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(
            Msg(co.Id, inRange, MessageStatus.Sent, billable: true),
            Msg(co.Id, inRange, MessageStatus.Delivered, billable: true),
            Msg(co.Id, inRange, MessageStatus.Read, billable: false),
            Msg(co.Id, inRange, MessageStatus.Failed, billable: false),
            Msg(co.Id, inRange, MessageStatus.Sent, billable: false, direction: MessageDirection.Inbound), // inbound excluded
            Msg(co.Id, outOfRange, MessageStatus.Sent, billable: true) // out of range excluded
        );
        await db.SaveChangesAsync();

        var svc = new AdminReportsService(db);
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var result = await svc.GetUsageAsync(from, to);

        var row = Assert.Single(result.Companies);
        Assert.Equal("Acme", row.CompanyName);
        Assert.Equal(3, row.Sent);       // Sent + Delivered + Read (4 outbound − 1 Failed)
        Assert.Equal(1, row.Delivered);
        Assert.Equal(1, row.Read);
        Assert.Equal(1, row.Failed);
        Assert.Equal(2, row.Billable);
        Assert.Equal(3, result.TotalSent);
        Assert.Equal(2, result.TotalBillable);
    }

    private static wa_api.Features.Bot.Entities.AiReplyLog AiLog(
        Guid companyId, DateTime createdAt, wa_api.Features.Bot.Entities.AiCallKind kind, decimal? cost,
        int cacheRead = 0, int cacheWrite = 0) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Kind = kind,
        Model = "claude-haiku-4-5",
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadTokens = cacheRead,
        CacheWriteTokens = cacheWrite,
        CostUsd = cost,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        IsActive = true,
    };

    [Fact]
    public async Task Usage_AddsAiAndMetaCost_AndComparesItToThePlan()
    {
        await using var db = NewDb();
        var co = Company("Marist");
        var plan = Plan("Growth", 5000, 1199m);
        plan.Currency = "AED";
        var inRange = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(co, plan, Sub(co.Id, plan.Id, inRange));

        var sent = Msg(co.Id, inRange, MessageStatus.Delivered, billable: true);
        sent.MetaCostUsd = 0.0157m;
        var free = Msg(co.Id, inRange, MessageStatus.Delivered, billable: false);
        free.MetaCostUsd = 0m;
        var notPricedYet = Msg(co.Id, inRange, MessageStatus.Sent, billable: true);
        db.AddRange(sent, free, notPricedYet);

        db.AddRange(
            AiLog(co.Id, inRange, wa_api.Features.Bot.Entities.AiCallKind.Reply, 0.012m, cacheWrite: 8700),
            AiLog(co.Id, inRange, wa_api.Features.Bot.Entities.AiCallKind.Reply, 0.003m, cacheRead: 8700),
            AiLog(co.Id, inRange, wa_api.Features.Bot.Entities.AiCallKind.Preview, 0.004m),
            AiLog(co.Id, inRange, wa_api.Features.Bot.Entities.AiCallKind.FollowUp, null));   // unpriced model
        await db.SaveChangesAsync();

        var result = await new AdminReportsService(db).GetUsageAsync(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var cost = Assert.Single(result.Companies).Cost!;
        Assert.Equal(0.019m, cost.AiCostUsd);           // previews are real spend and are included
        Assert.Equal(1, cost.UnpricedAiCalls);
        Assert.Equal(0.0157m, cost.MetaCostUsd);
        Assert.Equal(1, cost.UnpricedMetaMessages);
        Assert.Equal(0.0347m, cost.TotalCostUsd);
        Assert.Equal(0.13m, cost.TotalCostAed);         // 0.0347 × 3.6725, 2 dp
        Assert.Equal(0.5, cost.CacheHitRate);
        Assert.Equal("Growth", cost.PlanName);
        Assert.Equal(1199m, cost.PlanPriceForRangeAed); // a 30-day range is one month
        Assert.Equal(1199m - 0.13m, cost.MarginAed);
        Assert.Equal(3, cost.AiByKind.Count);
        Assert.Equal(0.0347m, result.TotalAiCostUsd + result.TotalMetaCostUsd);
    }

    [Fact]
    public async Task Usage_IncludesACompanyThatOnlySpentOnAi()
    {
        await using var db = NewDb();
        var co = Company("Quiet clinic");
        var inRange = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(co, AiLog(co.Id, inRange, wa_api.Features.Bot.Entities.AiCallKind.Preview, 0.004m));
        await db.SaveChangesAsync();

        var result = await new AdminReportsService(db).GetUsageAsync(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var row = Assert.Single(result.Companies);
        Assert.Equal(0, row.Sent);
        Assert.Equal(0.004m, row.Cost!.TotalCostUsd);
        Assert.Null(row.Cost.CostPerMessageAed);
        Assert.Null(row.Cost.PlanName);
    }
}
