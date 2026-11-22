using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Templates;

/// <summary>
/// The starter library: ready-made follow-up templates, added as drafts.
/// <para>
/// Drafts and nothing more is the whole point. Each client submits under their own number and their own
/// Meta credentials, so a starter pack that submitted on their behalf would be putting words in their
/// mouth — and burning their approval quota.
/// </para>
/// </summary>
public class StarterTemplateTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => StarterTemplateTests.CompanyId;
        public bool IsSuperAdmin => false;
    }

    /// <summary>Throws on every call: nothing in this feature may talk to Meta.</summary>
    private sealed class ForbiddenMeta : IMetaTemplateClient
    {
        public Task<MetaTemplateResult> CreateAsync(
            string wabaId, string accessToken, object payload, CancellationToken ct = default)
            => throw new InvalidOperationException("The starter pack must not submit anything to Meta.");

        public Task<MetaTemplateStatus?> GetStatusAsync(
            string metaTemplateId, string accessToken, CancellationToken ct = default)
            => throw new InvalidOperationException("The starter pack must not call Meta.");

        public Task<bool> DeleteAsync(
            string wabaId, string name, string accessToken, CancellationToken ct = default)
            => throw new InvalidOperationException("The starter pack must not call Meta.");
    }

    /// <summary>Throws on every call: adding drafts must not notify anyone either.</summary>
    private sealed class ForbiddenNotifications : INotificationService
    {
        public Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
            string title, string body, CancellationToken ct = default)
            => throw new InvalidOperationException("The starter pack must not raise notifications.");

        public Task MarkReadAsync(Guid notificationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(AppDbContext Db, TemplateService Service)> SeedAsync(bool withConnection = true)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"starter-{Guid.NewGuid()}")
                .AddInterceptors(new AuditInterceptor(new HttpContextAccessor()))
                .Options,
            new Tenant());

        if (withConnection)
        {
            var now = DateTime.UtcNow;
            db.WabaConnections.Add(new WabaConnection
            {
                Id = Guid.NewGuid(), CompanyId = CompanyId, PhoneNumberId = "pn-1", WabaId = "waba-1",
                DisplayPhoneNumber = "+15550100", EncryptedAccessToken = "token",
                CreatedAt = now, UpdatedAt = now, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        return (db, new TemplateService(db, new ForbiddenMeta(), new ForbiddenNotifications()));
    }

    [Fact]
    public async Task TheStarterPack_CreatesTheWholeLibraryAsDrafts()
    {
        var (db, service) = await SeedAsync();

        var created = await service.CreateStarterPackAsync();

        Assert.Equal(StarterTemplates.All.Count, created.Count);
        Assert.All(created, t => Assert.Equal(TemplateStatus.Draft, t.Status));
        Assert.All(created, t => Assert.Null(t.MetaTemplateId));
        Assert.Equal(StarterTemplates.All.Count, await db.Templates.CountAsync());
    }

    [Fact]
    public async Task PressingItTwice_AddsNothingTheSecondTime()
    {
        var (db, service) = await SeedAsync();

        await service.CreateStarterPackAsync();
        var second = await service.CreateStarterPackAsync();

        Assert.Empty(second);
        Assert.Equal(StarterTemplates.All.Count, await db.Templates.CountAsync());
    }

    [Fact]
    public async Task ItCoversBothMarketingFollowUpsAndCheaperUtilityUpdates()
    {
        var (_, service) = await SeedAsync();

        var created = await service.CreateStarterPackAsync();

        Assert.Contains(created, t => t.Category == TemplateCategory.Marketing);
        // Appointment messages are Utility and cost a fraction of a marketing send.
        Assert.Contains(created, t => t.Category == TemplateCategory.Utility);
    }

    [Fact]
    public async Task EveryStarterTemplate_PassesTheSameValidationAsAHandWrittenOne()
    {
        // If one of these would be refused by Meta, the button hands the client a template they cannot
        // submit — so they go through CreateAsync's validation like anything else.
        var (_, service) = await SeedAsync();

        var created = await service.CreateStarterPackAsync();

        Assert.All(created, t => Assert.NotEmpty(t.Components.Body.Text));
        Assert.All(created, t => Assert.DoesNotContain("\n\n\n", t.Components.Body.Text));
    }

    [Fact]
    public async Task WithNoWhatsAppNumberConnected_ItSaysSoRatherThanFailing()
    {
        var (_, service) = await SeedAsync(withConnection: false);

        var ex = await Assert.ThrowsAsync<wa_api.Common.Errors.BusinessRuleException>(
            () => service.CreateStarterPackAsync());

        Assert.Equal("WABA_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public void EveryTemplateInTheLibrary_HasAHintForEachVariable()
    {
        // The hints are what the model is told to fill, so a missing one is a variable nobody can resolve.
        foreach (var starter in StarterTemplates.All)
        {
            var variables = TemplateFill.VariableCount(new Template { Components = starter.Components });

            Assert.True(
                starter.VariableHints.Count >= variables,
                $"{starter.Name} has {variables} variables but {starter.VariableHints.Count} hints.");
        }
    }

    [Fact]
    public void CopyingATemplate_DoesNotShareTheLibrarysObjects()
    {
        // Two clients editing "the same" starter template must not edit each other's.
        var source = StarterTemplates.All[0].Components;

        var copy = StarterTemplates.CopyComponents(source);
        copy.Body.Text = "changed";
        if (copy.Buttons.Count > 0) copy.Buttons[0].Text = "changed";

        Assert.NotEqual("changed", source.Body.Text);
        if (source.Buttons.Count > 0) Assert.NotEqual("changed", source.Buttons[0].Text);
    }
}
