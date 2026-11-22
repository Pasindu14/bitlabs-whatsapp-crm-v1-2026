using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Dtos;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Saving the handoff alert settings. A number or template that could never work is refused at save time,
/// because the alternative is finding out when a customer is waiting and nobody was told.
/// </summary>
public class BotProfileHandoffSettingsTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => BotProfileHandoffSettingsTests.CompanyId;
        public bool IsSuperAdmin => false;
    }

    private static (AppDbContext Db, BotProfileService Service) Create()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"profile-{Guid.NewGuid()}")
                .AddInterceptors(new AuditInterceptor(new HttpContextAccessor()))
                .Options,
            new Tenant());

        return (db, new BotProfileService(db));
    }

    private static SaveBotProfileRequest Request(
        IReadOnlyList<string>? phones = null, Guid? templateId = null, int resumeMinutes = 0) =>
        new(true, "Clinic", "A clinic.", null, null, null, null, null, null, 500,
            HandoffAlertPhones: phones, HandoffAlertTemplateId: templateId, HandoffResumeMinutes: resumeMinutes);

    private static async Task<Guid> AddTemplateAsync(AppDbContext db, string body)
    {
        var template = new Template
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, WabaConnectionId = Guid.NewGuid(), WabaId = "w",
            Name = "alert", Language = "en", Category = TemplateCategory.Utility, Status = TemplateStatus.Approved,
            Components = new TemplateComponents { Body = new TemplateBody { Text = body } },
        };
        db.Templates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    [Fact]
    public async Task NumbersAreSavedAsDigitsOnly_WithoutDuplicates()
    {
        var (_, service) = Create();

        var saved = await service.SaveAsync(Request(["+94 77 123 4567", "94771234567", "  "]));

        Assert.Equal(["94771234567"], saved.HandoffAlertPhones);
    }

    [Fact]
    public async Task ANumberWithNoCountryCode_IsRefused()
    {
        var (_, service) = Create();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(Request(["0771234567"])));

        Assert.Equal("INVALID_ALERT_PHONE", ex.ErrorCode);
    }

    [Fact]
    public async Task MoreThanFiveNumbers_AreRefused()
    {
        var (_, service) = Create();
        string[] six = ["94770000001", "94770000002", "94770000003", "94770000004", "94770000005", "94770000006"];

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(Request(six)));

        Assert.Equal("TOO_MANY_ALERT_PHONES", ex.ErrorCode);
    }

    [Fact]
    public async Task LeavingTheListOut_KeepsWhatWasSaved()
    {
        var (_, service) = Create();
        await service.SaveAsync(Request(["94771234567"]));

        var saved = await service.SaveAsync(Request(phones: null));

        Assert.Equal(["94771234567"], saved.HandoffAlertPhones);
    }

    [Fact]
    public async Task ATemplateWithTheWrongNumberOfVariables_IsRefused()
    {
        var (db, service) = Create();
        var id = await AddTemplateAsync(db, "Hi {{1}}, someone needs you.");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(Request(templateId: id)));

        Assert.Equal("INVALID_ALERT_TEMPLATE", ex.ErrorCode);
    }

    [Fact]
    public async Task AThreeVariableTemplate_IsAccepted()
    {
        var (db, service) = Create();
        var id = await AddTemplateAsync(db, "Customer {{1}}, +{{2}}. Summary: {{3}}. Please call.");

        var saved = await service.SaveAsync(Request(templateId: id, resumeMinutes: 90));

        Assert.Equal(id, saved.HandoffAlertTemplateId);
        Assert.Equal(90, saved.HandoffResumeMinutes);
    }
}
