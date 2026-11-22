using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.StaffAlerts;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// The team's handoff alert: an in-app notification and a WhatsApp template to each listed number.
/// <para>
/// The bot has already paused and told the customer someone will be in touch by the time this runs, so
/// the contract that matters most is the negative one: nothing here may throw, and one bad number or
/// one unapproved template must never stop the rest of the team being told.
/// </para>
/// </summary>
public class StaffAlertServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => StaffAlertServiceTests.CompanyId;
        public bool IsSuperAdmin => false;
    }

    private sealed record SentTemplate(
        string To, string Name, string Language, IReadOnlyList<string> Parameters, string Rendered);

    private sealed class RecordingSender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<SentTemplate> Sent { get; } = [];

        /// <summary>Numbers whose send throws, to prove the others still go out.</summary>
        public HashSet<string> Failing { get; } = [];

        public Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
            => throw new InvalidOperationException("The alert is a template, never free text.");

        public async Task<Message> SendTemplateAsync(
            Contact contact, WabaConnection waba, string templateName, string language,
            IReadOnlyList<string> parameters, string renderedBody, Guid? conversationId,
            CancellationToken ct = default)
        {
            if (Failing.Contains(contact.Phone)) throw new HttpRequestException("unreachable");

            Sent.Add(new SentTemplate(contact.Phone, templateName, language, parameters, renderedBody));

            var message = new Message
            {
                Id = Guid.NewGuid(), CompanyId = contact.CompanyId, ContactId = contact.Id,
                WabaConnectionId = waba.Id, ConversationId = conversationId, Body = renderedBody,
                Direction = MessageDirection.Outbound, Status = MessageStatus.Accepted,
            };
            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);
            return message;
        }
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(string Title, string Body)> Created { get; } = [];

        public Task CreateAsync(Guid companyId, NotificationType type, string title, string body,
            CancellationToken ct = default)
        {
            Created.Add((title, body));
            return Task.CompletedTask;
        }

        public Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
            string title, string body, CancellationToken ct = default)
            => CreateAsync(companyId, type, title, body, ct);

        public Task MarkReadAsync(Guid notificationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed record Fixture(
        AppDbContext Db, RecordingSender Sender, RecordingNotifications Notifications,
        StaffAlertService Service, Contact Customer, Guid ConversationId);

    private static async Task<Fixture> SeedAsync(
        TemplateStatus status = TemplateStatus.Approved,
        string[]? teamPhones = null,
        bool withTemplate = true,
        string body = "A customer needs the team: {{1}}, +{{2}}. Summary: {{3}}. Please contact them.")
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"staff-alert-{Guid.NewGuid()}")
                .AddInterceptors(new AuditInterceptor(new HttpContextAccessor()))
                .Options,
            new Tenant());

        var now = DateTime.UtcNow;
        var waba = new WabaConnection
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, PhoneNumberId = "pn-1", WabaId = "waba-1",
            DisplayPhoneNumber = "+15550100", EncryptedAccessToken = "token",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };
        var customer = new Contact
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Phone = "94751803295", Name = "Sarah",
        };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = customer.Id, WabaConnectionId = waba.Id,
            LastMessageAt = now,
        };

        Guid? templateId = null;
        if (withTemplate)
        {
            var template = new Template
            {
                Id = Guid.NewGuid(), CompanyId = CompanyId, WabaConnectionId = waba.Id, WabaId = "waba-1",
                Name = "staff_handoff_alert", Language = "en", Category = TemplateCategory.Utility,
                Status = status, Components = new TemplateComponents { Body = new TemplateBody { Text = body } },
            };
            templateId = template.Id;
            db.Templates.Add(template);
        }

        db.BotProfiles.Add(new BotProfile
        {
            CompanyId = CompanyId, BusinessName = "Clinic", BusinessDescription = "A clinic.",
            HandoffAlertPhones = [.. teamPhones ?? ["94770000001", "94770000002"]],
            HandoffAlertTemplateId = templateId,
        });
        db.WabaConnections.Add(waba);
        db.Contacts.Add(customer);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var sender = new RecordingSender(db);
        var notifications = new RecordingNotifications();
        var service = new StaffAlertService(db, sender, notifications, NullLogger<StaffAlertService>.Instance);

        return new Fixture(db, sender, notifications, service, customer, conversation.Id);
    }

    private static StaffAlert AlertFor(Fixture f, string summary = "Asked about the consultation fee", bool inApp = true) =>
        new(CompanyId, f.Customer.Id, f.ConversationId, summary, inApp);

    [Fact]
    public async Task EachTeamNumber_GetsTheTemplate_WithNameNumberAndSummary()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Equal(["94770000001", "94770000002"], f.Sender.Sent.Select(s => s.To));
        var sent = f.Sender.Sent[0];
        Assert.Equal("staff_handoff_alert", sent.Name);
        Assert.Equal(["Sarah", "94751803295", "Asked about the consultation fee"], sent.Parameters);
        Assert.Contains("+94751803295", sent.Rendered);
    }

    [Fact]
    public async Task TheTeamIsAlsoToldInTheApp()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f));

        var note = Assert.Single(f.Notifications.Created);
        Assert.Contains("+94751803295", note.Body);
        Assert.Contains("Asked about the consultation fee", note.Body);
    }

    [Fact]
    public async Task WhenTheCallerAlreadyRaisedItsOwnNotification_NoSecondOneIsAdded()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f, inApp: false));

        Assert.Empty(f.Notifications.Created);
        Assert.Equal(2, f.Sender.Sent.Count);
    }

    [Fact]
    public async Task WithNoTemplateChosen_OnlyTheInAppNotificationGoesOut()
    {
        var f = await SeedAsync(withTemplate: false);

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Empty(f.Sender.Sent);
        Assert.Single(f.Notifications.Created);
    }

    [Theory]
    [InlineData(TemplateStatus.Pending)]
    [InlineData(TemplateStatus.Rejected)]
    [InlineData(TemplateStatus.Paused)]
    [InlineData(TemplateStatus.Draft)]
    public async Task ATemplateMetaHasNotApproved_IsNeverSent(TemplateStatus status)
    {
        var f = await SeedAsync(status);

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Empty(f.Sender.Sent);
        Assert.Single(f.Notifications.Created);
    }

    [Fact]
    public async Task ATemplateThatDoesNotTakeThreeVariables_IsNeverSent()
    {
        // Meta would refuse a parameter count that does not match what it approved.
        var f = await SeedAsync(body: "Hi {{1}}, a customer needs you.");

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Empty(f.Sender.Sent);
    }

    [Fact]
    public async Task WithNoTeamNumbers_NothingIsSentOverWhatsApp()
    {
        var f = await SeedAsync(teamPhones: []);

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Empty(f.Sender.Sent);
        Assert.Single(f.Notifications.Created);
    }

    [Fact]
    public async Task OneUnreachableNumber_DoesNotStopTheNextOneBeingTold()
    {
        var f = await SeedAsync(teamPhones: ["94770000001", "94770000002"]);
        f.Sender.Failing.Add("94770000001");

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Equal(["94770000002"], f.Sender.Sent.Select(s => s.To));
    }

    [Fact]
    public async Task ACustomerWhoIsAlsoOnTheTeamList_IsNotMessagedAboutThemselves()
    {
        var f = await SeedAsync(teamPhones: ["94751803295", "94770000002"]);

        await f.Service.AlertAsync(AlertFor(f));

        Assert.Equal(["94770000002"], f.Sender.Sent.Select(s => s.To));
    }

    [Fact]
    public async Task ASummaryWithNewlines_IsFlattenedBecauseMetaRefusesThem()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f, "Wants a price.\n\nAlso asked\tabout  PRP."));

        var summary = f.Sender.Sent[0].Parameters[2];
        Assert.DoesNotContain('\n', summary);
        Assert.DoesNotContain('\t', summary);
        Assert.DoesNotContain("  ", summary);
    }

    [Fact]
    public async Task ATooLongSummary_IsCutRatherThanDropped()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f, new string('x', 900)));

        var summary = f.Sender.Sent[0].Parameters[2];
        Assert.True(summary.Length <= StaffAlertService.MaxSummaryLength);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public async Task ATeamNumberGetsAContactSoTheMessageHasSomewhereToLive()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(AlertFor(f));

        Assert.True(await f.Db.Contacts.AnyAsync(c => c.CompanyId == CompanyId && c.Phone == "94770000001"));
    }

    [Fact]
    public async Task AnUnknownContact_DoesNothingAndDoesNotThrow()
    {
        var f = await SeedAsync();

        await f.Service.AlertAsync(new StaffAlert(CompanyId, Guid.NewGuid(), null, "anything"));

        Assert.Empty(f.Sender.Sent);
        Assert.Empty(f.Notifications.Created);
    }
}
