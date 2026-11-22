using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.StaffAlerts;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// What the bot's handoff tool does: it pauses the thread and — new — tells the team who is waiting and
/// why. Before this the pause was silent: the customer was told someone would be in touch, and nobody was.
/// </summary>
public class BotHandoffTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => BotHandoffTests.CompanyId;
        public bool IsSuperAdmin => false;
    }

    private sealed class RecordingAlerts : IStaffAlertService
    {
        public List<StaffAlert> Alerts { get; } = [];

        public Task AlertAsync(StaffAlert alert, CancellationToken ct = default)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private static async Task<(AppDbContext Db, Conversation Conversation, BotToolExecutor Executor, RecordingAlerts Alerts)>
        SeedAsync(int handoffResumeMinutes = 0, bool dryRun = false)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"handoff-{Guid.NewGuid()}")
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
        var contact = new Contact { Id = Guid.NewGuid(), CompanyId = CompanyId, Phone = "94751803295", Name = "Sarah" };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id, WabaConnectionId = waba.Id,
            LastMessageAt = now,
        };

        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.BotProfiles.Add(new BotProfile
        {
            CompanyId = CompanyId, BusinessName = "Clinic", BusinessDescription = "A clinic.",
            HandoffResumeMinutes = handoffResumeMinutes,
        });
        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id, WabaConnectionId = waba.Id,
            ConversationId = conversation.Id, Body = "Ok, then you have prp?",
            Direction = MessageDirection.Inbound, Status = MessageStatus.Delivered, CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var alerts = new RecordingAlerts();
        var executor = new BotToolExecutor(
            db, availability: null!, appointments: null!,
            new BotToolScope(CompanyId, contact.Id, conversation.Id, TimeZoneInfo.Utc, dryRun),
            NullLogger<BotToolExecutor>.Instance,
            crm: null, staffAlerts: alerts);

        return (db, conversation, executor, alerts);
    }

    private static Dictionary<string, JsonElement> Reason(string reason) =>
        new() { ["reason"] = JsonSerializer.SerializeToElement(reason) };

    [Fact]
    public async Task AHandoff_PausesTheThread_AndTellsTheTeamWhy()
    {
        var (db, conversation, executor, alerts) = await SeedAsync();

        var result = await executor.ExecuteAsync(
            BotTools.HandoffToHuman, Reason("Needs the consultation fee confirmed."), CancellationToken.None);

        Assert.True(result.HandedOff);
        var saved = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(ConversationBotState.Paused, saved.BotState);

        var alert = Assert.Single(alerts.Alerts);
        Assert.Equal(conversation.ContactId, alert.ContactId);
        Assert.Equal(conversation.Id, alert.ConversationId);
        Assert.True(alert.InApp);
        // The reason AND the question the customer actually asked: the reason alone means little.
        Assert.Contains("Needs the consultation fee confirmed", alert.Summary);
        Assert.Contains("Ok, then you have prp?", alert.Summary);
    }

    [Fact]
    public async Task ByDefault_AHandoffStaysPausedUntilSomeoneHandsItBack()
    {
        var (db, conversation, executor, _) = await SeedAsync(handoffResumeMinutes: 0);

        await executor.ExecuteAsync(BotTools.HandoffToHuman, Reason("x"), CancellationToken.None);

        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == conversation.Id)).BotResumesAt);
    }

    [Fact]
    public async Task WhenTheBusinessSetsASafetyNet_TheBotComesBackOnItsOwn()
    {
        var (db, conversation, executor, _) = await SeedAsync(handoffResumeMinutes: 120);

        await executor.ExecuteAsync(BotTools.HandoffToHuman, Reason("x"), CancellationToken.None);

        var resumes = (await db.Conversations.SingleAsync(c => c.Id == conversation.Id)).BotResumesAt;
        Assert.NotNull(resumes);
        Assert.InRange(resumes!.Value, DateTime.UtcNow.AddMinutes(118), DateTime.UtcNow.AddMinutes(122));
    }

    [Fact]
    public async Task APreviewHandoff_PausesNothingAndAlertsNobody()
    {
        var (db, conversation, executor, alerts) = await SeedAsync(dryRun: true);

        var result = await executor.ExecuteAsync(BotTools.HandoffToHuman, Reason("x"), CancellationToken.None);

        Assert.True(result.HandedOff);
        Assert.Equal(ConversationBotState.Active, (await db.Conversations.SingleAsync(c => c.Id == conversation.Id)).BotState);
        Assert.Empty(alerts.Alerts);
    }
}
