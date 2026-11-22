using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins when a paused thread is handed back to the bot.
/// <para>
/// The open-ended case is the one that matters: an agent took a thread over on 17 Sep, moved on, and the
/// customer's "Hi" five days later was met with silence because nothing ever un-paused it. A takeover
/// must expire on its own, but never while someone is still talking.
/// </para>
/// </summary>
public class BotAutoResumeJobTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed record Fixture(AppDbContext Db, Conversation Conversation);

    private static async Task<Fixture> SeedAsync(
        DateTime? resumesAt, DateTime lastMessageAt, DateTime? pausedAt = null, int idleHours = 24)
    {
        var tenant = new ScopedTenantContext(new HttpContextAccessor());
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"auto-resume-{Guid.NewGuid()}").Options,
            tenant);

        var now = DateTime.UtcNow;

        var waba = new WabaConnection
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, PhoneNumberId = "pn-1", WabaId = "waba-1",
            DisplayPhoneNumber = "+15550100", EncryptedAccessToken = "token",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var contact = new Contact
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Phone = "15550111", Name = "Charith",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id,
            WabaConnectionId = waba.Id, Status = ConversationStatus.Open,
            BotState = ConversationBotState.Paused,
            BotPausedAt = pausedAt ?? lastMessageAt,
            BotResumesAt = resumesAt,
            LastMessageAt = lastMessageAt,
            LastMessageDirection = MessageDirection.Inbound,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.BotProfiles.Add(new BotProfile
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, IsEnabled = true,
            BusinessName = "the clinic", BusinessDescription = "A doctor's clinic.",
            ResumeBotAfterIdleHours = idleHours,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        });

        await db.SaveChangesAsync();
        return new Fixture(db, conversation);
    }

    private static async Task<ConversationBotState> RunAsync(Fixture f)
    {
        await new BotAutoResumeJob(f.Db, NullLogger<BotAutoResumeJob>.Instance)
            .RunAsync(CancellationToken.None);

        return (await f.Db.Conversations.IgnoreQueryFilters().FirstAsync()).BotState;
    }

    [Fact]
    public async Task ATakeoverWindowThatHasExpired_GoesBackToTheBot()
    {
        var f = await SeedAsync(
            resumesAt: DateTime.UtcNow.AddMinutes(-1), lastMessageAt: DateTime.UtcNow.AddMinutes(-90));

        Assert.Equal(ConversationBotState.Active, await RunAsync(f));
    }

    [Fact]
    public async Task ATakeoverStillRunning_IsLeftAlone()
    {
        var f = await SeedAsync(
            resumesAt: DateTime.UtcNow.AddMinutes(30), lastMessageAt: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(ConversationBotState.Paused, await RunAsync(f));
    }

    [Fact]
    public async Task AnOpenEndedPause_ExpiresOnceTheThreadHasBeenQuietLongEnough()
    {
        // The live failure: paused indefinitely, then days of silence.
        var f = await SeedAsync(resumesAt: null, lastMessageAt: DateTime.UtcNow.AddHours(-30));

        Assert.Equal(ConversationBotState.Active, await RunAsync(f));
    }

    [Fact]
    public async Task AnOpenEndedPause_StaysWhileTheConversationIsStillMoving()
    {
        // An agent mid-conversation must never have the bot walk back in over them.
        var f = await SeedAsync(resumesAt: null, lastMessageAt: DateTime.UtcNow.AddHours(-2));

        Assert.Equal(ConversationBotState.Paused, await RunAsync(f));
    }

    [Fact]
    public async Task APauseOnAnAlreadyOldThread_GetsItsFullGracePeriod()
    {
        // Taking over a thread that went quiet last week must not expire on the next sweep.
        var f = await SeedAsync(
            resumesAt: null,
            lastMessageAt: DateTime.UtcNow.AddDays(-7),
            pausedAt: DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(ConversationBotState.Paused, await RunAsync(f));
    }

    [Fact]
    public async Task IdleResumeTurnedOff_KeepsTheThreadWithThePersonForever()
    {
        var f = await SeedAsync(
            resumesAt: null, lastMessageAt: DateTime.UtcNow.AddDays(-30), idleHours: 0);

        Assert.Equal(ConversationBotState.Paused, await RunAsync(f));
    }
}
