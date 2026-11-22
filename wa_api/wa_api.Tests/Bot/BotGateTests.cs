using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Common.Tenancy;
using wa_api.Features.Auth;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Entities;
using wa_api.Features.ClinicCrm.Entities;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// The two checks that keep the chatbot from running where it should not (plan 005): the plan must include
/// it, and the platform switch must be on. Both run from the webhook job, which has NO tenant — so every
/// test here uses a context with no company, proving the queries do not lean on the tenant filter.
/// </summary>
public class BotGateTests
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => false;
    }

    private sealed class Tenant(Guid? companyId, bool superAdmin = false) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin { get; } = superAdmin;
    }

    private static AppDbContext NewDb(ITenantContext? tenant = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"bot-gate-{Guid.NewGuid()}")
                .Options,
            tenant ?? new NoTenant());

    private static async Task SubscribeAsync(
        AppDbContext db, Guid companyId, string[] flags,
        SubscriptionStatus status = SubscriptionStatus.Active, int periodEndsInDays = 20, int createdDaysAgo = 1)
    {
        var now = DateTime.UtcNow;
        var plan = new Plan
        {
            Id = Guid.NewGuid(), Name = $"Plan {Guid.NewGuid()}", MonthlyMessageQuota = 1000, Price = 10m,
            FeatureFlags = [.. flags], CreatedAt = now, UpdatedAt = now, IsActive = true,
        };
        db.Plans.Add(plan);
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PlanId = plan.Id, Status = status,
            CurrentPeriodStart = now.AddDays(-10), CurrentPeriodEnd = now.AddDays(periodEndsInDays),
            CreatedAt = now.AddDays(-createdDaysAgo), UpdatedAt = now, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private static BotGate NewGate(AppDbContext db, bool enabled = true, int cacheSeconds = 0) => new(
        db,
        new PlanFeatureGate(db, new NoTenant()),
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new BotOptions { Enabled = enabled, GateCacheSeconds = cacheSeconds }));

    private static async Task EnableProfileAsync(AppDbContext db, Guid companyId, bool enabled = true)
    {
        var now = DateTime.UtcNow;
        db.BotProfiles.Add(new BotProfile
        {
            Id = Guid.NewGuid(), CompanyId = companyId, IsEnabled = enabled, BusinessName = "Shop",
            BusinessDescription = "A shop.", CreatedAt = now, UpdatedAt = now, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    // ── Plan feature gate ────────────────────────────────────────────────────

    [Fact]
    public async Task APlanWithTheFlag_HasTheFeature_AndAPlanWithoutItDoesNot()
    {
        await using var db = NewDb();
        var withBot = Guid.NewGuid();
        var without = Guid.NewGuid();
        await SubscribeAsync(db, withBot, [Permission.LiveMessage, Permission.ManageBot]);
        await SubscribeAsync(db, without, [Permission.LiveMessage, Permission.ScheduleCampaign]);
        var gate = new PlanFeatureGate(db, new NoTenant());

        Assert.True(await gate.HasFeatureAsync(withBot, Permission.ManageBot));
        Assert.False(await gate.HasFeatureAsync(without, Permission.ManageBot));
        Assert.False(await gate.HasFeatureAsync(withBot, Permission.ManageCalendar));   // one flag is not the other
    }

    [Fact]
    public async Task NoSubscription_AnInactiveOne_OrAnExpiredOne_HasNoFeatures()
    {
        await using var db = NewDb();
        var none = Guid.NewGuid();
        var cancelled = Guid.NewGuid();
        var expired = Guid.NewGuid();
        await SubscribeAsync(db, cancelled, [Permission.ManageBot], SubscriptionStatus.Cancelled);
        await SubscribeAsync(db, expired, [Permission.ManageBot], periodEndsInDays: -1);
        var gate = new PlanFeatureGate(db, new NoTenant());

        Assert.False(await gate.HasFeatureAsync(none, Permission.ManageBot));
        Assert.False(await gate.HasFeatureAsync(cancelled, Permission.ManageBot));
        Assert.False(await gate.HasFeatureAsync(expired, Permission.ManageBot));
    }

    [Fact]
    public async Task TheNewestActiveSubscription_IsTheOneThatCounts()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.ManageBot], createdDaysAgo: 30);   // old plan had it
        await SubscribeAsync(db, company, [Permission.LiveMessage], createdDaysAgo: 1);   // downgraded since
        var gate = new PlanFeatureGate(db, new NoTenant());

        Assert.False(await gate.HasFeatureAsync(company, Permission.ManageBot));
    }

    [Fact]
    public async Task EnsureFeature_ThrowsForATenantWithoutIt_AndLetsSuperAdminThrough()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.LiveMessage]);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new PlanFeatureGate(db, new Tenant(company)).EnsureFeatureAsync(Permission.ManageBot));
        Assert.Equal("PLAN_FEATURE_REQUIRED", ex.ErrorCode);

        await new PlanFeatureGate(db, new Tenant(null, superAdmin: true)).EnsureFeatureAsync(Permission.ManageBot);
    }

    // ── Bot gate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlatformSwitchOff_NobodyIsLive_HoweverTheyAreSetUp()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.ManageBot]);
        await EnableProfileAsync(db, company);

        Assert.False(await NewGate(db, enabled: false).IsLiveAsync(company));
        Assert.True(await NewGate(db, enabled: true).IsLiveAsync(company));
    }

    [Fact]
    public async Task APlanWithoutTheChatbot_IsNeverLive_EvenWithAnEnabledProfile()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.LiveMessage, Permission.ScheduleCampaign]);
        await EnableProfileAsync(db, company);

        Assert.False(await NewGate(db).IsLiveAsync(company));
    }

    [Fact]
    public async Task Entitled_ButNothingSetUp_IsNotLive_UntilAProfileOrARuleIsEnabled()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.ManageBot]);
        var gate = NewGate(db);

        Assert.False(await gate.IsLiveAsync(company));

        await EnableProfileAsync(db, company, enabled: false);          // saved, but switched off
        Assert.False(await gate.IsLiveAsync(company));

        var now = DateTime.UtcNow;
        db.AutoReplyRules.Add(new AutoReplyRule
        {
            Id = Guid.NewGuid(), CompanyId = company, Name = "hours", ReplyBody = "9 to 5", IsEnabled = true,
            Terms = ["hours"], CreatedAt = now, UpdatedAt = now, IsActive = true,
        });
        await db.SaveChangesAsync();

        Assert.True(await gate.IsLiveAsync(company));                   // a keyword rule alone is enough
    }

    [Fact]
    public async Task OneCompanysSetup_NeverMakesAnotherCompanyLive()
    {
        await using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SubscribeAsync(db, a, [Permission.ManageBot]);
        await SubscribeAsync(db, b, [Permission.ManageBot]);
        await EnableProfileAsync(db, a);
        var gate = NewGate(db);

        Assert.True(await gate.IsLiveAsync(a));
        Assert.False(await gate.IsLiveAsync(b));
    }

    [Fact]
    public async Task TheAnswerIsCached_AndForgetDropsIt()
    {
        await using var db = NewDb();
        var company = Guid.NewGuid();
        await SubscribeAsync(db, company, [Permission.ManageBot]);
        var gate = NewGate(db, cacheSeconds: 300);

        Assert.False(await gate.IsLiveAsync(company));
        await EnableProfileAsync(db, company);
        Assert.False(await gate.IsLiveAsync(company));                  // still the remembered answer

        gate.Forget(company);
        Assert.True(await gate.IsLiveAsync(company));
    }

    [Fact]
    public async Task ClinicCrm_IsOnlyReportedForACompanyWithAnEnabledConnection()
    {
        await using var db = NewDb();
        var connected = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.ClinicCrmConnections.AddRange(
            new ClinicCrmConnection
            {
                Id = Guid.NewGuid(), CompanyId = connected, BaseUrl = "https://crm.example", ClientId = "id",
                ClientSecret = "secret", IsEnabled = true, CreatedAt = now, UpdatedAt = now, IsActive = true,
            },
            new ClinicCrmConnection
            {
                Id = Guid.NewGuid(), CompanyId = disabled, BaseUrl = "https://crm.example", ClientId = "id",
                ClientSecret = "secret", IsEnabled = false, CreatedAt = now, UpdatedAt = now, IsActive = true,
            });
        await db.SaveChangesAsync();
        var gate = NewGate(db);

        Assert.True(await gate.HasClinicCrmAsync(connected));
        Assert.False(await gate.HasClinicCrmAsync(disabled));
        Assert.False(await gate.HasClinicCrmAsync(Guid.NewGuid()));
    }
}
