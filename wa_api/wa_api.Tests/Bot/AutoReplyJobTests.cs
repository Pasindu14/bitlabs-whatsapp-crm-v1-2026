using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins the guards in <see cref="AutoReplyJob"/> — the rules that decide whether the bot is allowed to
/// speak at all. These matter more than the matching itself: replying to an opted-out contact is a Meta
/// policy violation, and a missing loop-breaker or cooldown turns one customer message into a send storm.
/// Backed by EF Core InMemory, mirroring InboundMediaWebhookHandlerTests.
/// </summary>
public class AutoReplyJobTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        // Background jobs have no tenant context — the job reads with IgnoreQueryFilters.
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => false;
    }

    /// <summary>
    /// Records what was sent, so a test can assert the bot spoke — or stayed silent.
    /// <para>
    /// Mirrors the real <c>WhatsAppMessageSender</c> in the one respect the job depends on: it ADDS AND
    /// COMMITS the message row itself (WhatsAppMessageSender.cs:73-74), before the caller can stamp
    /// <c>IsAutoReply</c>. A fake that only returned a detached object would hide the retry window the
    /// job's idempotency guard exists to close.
    /// </para>
    /// </summary>
    private sealed class RecordingSender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<(string Body, bool ChargeCredit)> Sent { get; } = [];

        public async Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            Sent.Add((body, chargeCredit));

            var message = new Message
            {
                Id = Guid.NewGuid(),
                CompanyId = contact.CompanyId,
                ContactId = contact.Id,
                WabaConnectionId = waba.Id,
                ConversationId = conversationId,
                Body = body,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Sent,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
            };

            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);

            return message;
        }
    }

    private sealed class NoopNotifier : IChatNotifier
    {
        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MessageDeletedAsync(
            Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task BotStateChangedAsync(
            Guid companyId, ConversationResponse conversation, CancellationToken ct = default) => Task.CompletedTask;

        public Task BotTypingAsync(
            Guid companyId, Guid conversationId, bool isTyping, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MessageStatusAsync(
            Guid companyId, Guid conversationId, Guid messageId, MessageStatus status, DateTime? statusAt,
            string? errorMessage, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"auto-reply-{Guid.NewGuid()}")
                .Options,
            new Tenant());

    /// <summary>
    /// Seeds a healthy world: an active WABA, an opted-in contact, an open conversation whose 24-hour
    /// window is in the future, one inbound message, and one Fallback rule that answers anything.
    /// Each test then breaks exactly one of those preconditions.
    /// </summary>
    private static async Task<(AppDbContext Db, Message Inbound, Conversation Conversation, Contact Contact)>
        SeedAsync(bool withRule = true)
    {
        var db = NewDb();
        var now = DateTime.UtcNow;

        var waba = new WabaConnection
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            PhoneNumberId = "pn-1",
            WabaId = "waba-1",
            DisplayPhoneNumber = "+15550100",
            EncryptedAccessToken = "token",
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            Phone = "15550111",
            Name = "Mona",
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            Status = ConversationStatus.Open,
            LastMessageAt = now,
            WindowExpiresAt = now.AddHours(24),      // window OPEN
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

        var inbound = new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = contact.Id,
            WabaConnectionId = waba.Id,
            ConversationId = conversation.Id,
            Body = "hello",
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.Messages.Add(inbound);

        if (withRule)
        {
            db.AutoReplyRules.Add(new AutoReplyRule
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyId,
                Name = "catch-all",
                Trigger = AutoReplyTrigger.Fallback,
                ReplyBody = "Thanks for your message!",
                Priority = 0,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        return (db, inbound, conversation, contact);
    }

    private static (AutoReplyJob Job, RecordingSender Sender) NewJob(AppDbContext db)
    {
        var sender = new RecordingSender(db);
        var job = new AutoReplyJob(
            db,
            new AutoReplyEngine(NullLogger<AutoReplyEngine>.Instance),
            sender,
            new NoopNotifier(),
            NullLogger<AutoReplyJob>.Instance);
        return (job, sender);
    }

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task Replies_ToAnInboundMessage_WhenARuleMatches()
    {
        var (db, inbound, conversation, _) = await SeedAsync();
        var (job, sender) = NewJob(db);

        await job.RunAsync(inbound.Id, CancellationToken.None);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal("Thanks for your message!", sent.Body);

        // In-window replies are FREE — Meta bills the whole 24h window as one conversation.
        Assert.False(sent.ChargeCredit);

        // The reply is flagged, which is what makes the loop-breaker and cooldown work.
        var reply = await db.Messages.IgnoreQueryFilters()
            .SingleAsync(m => m.Direction == MessageDirection.Outbound);
        Assert.True(reply.IsAutoReply);

        // Thread snapshot advanced, but the window anchor was NOT touched.
        var updated = await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(MessageDirection.Outbound, updated.LastMessageDirection);
        Assert.Equal("Thanks for your message!", updated.LastMessageBody);
        Assert.Equal(conversation.WindowExpiresAt, updated.WindowExpiresAt);
    }

    [Fact]
    public async Task StaysSilent_WhenNoRulesExist()
    {
        var (db, inbound, _, _) = await SeedAsync(withRule: false);
        var (job, sender) = NewJob(db);

        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    // ── Guard 3: opt-out is a hard stop ───────────────────────────────────────

    [Fact]
    public async Task StaysSilent_WhenTheContactHasOptedOut()
    {
        var (db, inbound, _, contact) = await SeedAsync();
        contact.IsOptedOut = true;
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        // Replying after a STOP is a Meta policy violation, not merely impolite.
        Assert.Empty(sender.Sent);
    }

    // ── Guard 1 + 2: direction and the loop breaker ───────────────────────────

    [Fact]
    public async Task StaysSilent_ForAnOutboundMessage()
    {
        var (db, inbound, _, _) = await SeedAsync();
        inbound.Direction = MessageDirection.Outbound;
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task StaysSilent_ForAMessageThatIsItselfAnAutoReply()
    {
        // The loop breaker: a bot reply must never provoke another bot reply.
        var (db, inbound, _, _) = await SeedAsync();
        inbound.IsAutoReply = true;
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    // ── Guard 4: the 24-hour window ───────────────────────────────────────────

    [Fact]
    public async Task StaysSilent_WhenTheCustomerServiceWindowHasClosed()
    {
        var (db, inbound, conversation, _) = await SeedAsync();
        conversation.WindowExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    // ── Guard 5: cooldown + idempotency ───────────────────────────────────────

    [Fact]
    public async Task StaysSilent_WhenAnotherAutoReplyWasSentWithinTheCooldown()
    {
        var (db, inbound, conversation, contact) = await SeedAsync();

        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = contact.Id,
            WabaConnectionId = conversation.WabaConnectionId,
            ConversationId = conversation.Id,
            Body = "an earlier bot reply",
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            IsAutoReply = true,
            CreatedAt = DateTime.UtcNow.AddSeconds(-5),   // well inside the 60s cooldown
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task IsIdempotent_ARetryAfterASuccessfulSendDoesNotSendTwice()
    {
        var (db, inbound, _, _) = await SeedAsync();
        var (job, sender) = NewJob(db);

        await job.RunAsync(inbound.Id, CancellationToken.None);
        await job.RunAsync(inbound.Id, CancellationToken.None);   // Hangfire retry

        Assert.Single(sender.Sent);
    }

    // ── Unresolvable inputs ───────────────────────────────────────────────────

    [Fact]
    public async Task Throws_WhenTheMessageIsNotVisibleYet()
    {
        // Racing the webhook commit: throwing is what makes Hangfire retry, by which point the
        // batch has committed. A genuinely-absent id simply dead-letters after its retries.
        var (db, _, _, _) = await SeedAsync();
        var (job, _) = NewJob(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.RunAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StaysSilent_WhenTheWabaConnectionIsInactive()
    {
        var (db, inbound, conversation, _) = await SeedAsync();
        var waba = await db.WabaConnections.IgnoreQueryFilters()
            .SingleAsync(w => w.Id == conversation.WabaConnectionId);
        waba.IsActive = false;
        await db.SaveChangesAsync();

        var (job, sender) = NewJob(db);
        await job.RunAsync(inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }
}
