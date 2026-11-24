using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.ClinicCrm.Jobs;
using wa_api.Features.Messages.InboundMedia;
using wa_api.Features.Webhooks.Handlers;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Features.Webhooks.Processing;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Webhooks;

/// <summary>
/// Pins the inbound-media path of <see cref="InboundMessageWebhookHandler"/>: a customer's image is recorded
/// with its media metadata (so the row knows to render an image), the bytes are NOT fetched inline (a download
/// job is enqueued for after the batch commit), and the realtime push advertises the media as not-yet-ready.
/// A plain text message must not enqueue anything. Backed by EF Core InMemory (mirrors QuotaRefundOnFailureTests).
/// </summary>
public class InboundMediaWebhookHandlerTests
{
    private sealed class Tenant : ITenantContext
    {
        // Webhook scope has no tenant context — the handler stamps CompanyId explicitly + IgnoreQueryFilters.
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => false;
    }

    /// <summary>Records the realtime pushes so the test can assert the media DTO shape.</summary>
    private sealed class RecordingNotifier : IChatNotifier
    {
        public ConversationMessageResponse? Last { get; private set; }
        public int Count { get; private set; }

        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default)
        {
            Count++;
            Last = message;
            return Task.CompletedTask;
        }

        public Task MessageDeletedAsync(
            Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BotStateChangedAsync(
            Guid companyId, ConversationResponse conversation, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BotTypingAsync(
            Guid companyId, Guid conversationId, bool isTyping, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MessageStatusAsync(
            Guid companyId, Guid conversationId, Guid messageId, MessageStatus status, DateTime? statusAt,
            string? errorMessage, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Fake job client — Hangfire's Enqueue&lt;T&gt; is an extension over Create(Job, IState).</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        /// <summary>Every job but the clinic-CRM lead save, which is recorded in <see cref="Leads"/>.</summary>
        public List<Job> Created { get; } = [];
        public List<Job> Leads { get; } = [];
        /// <summary>The clinic-CRM "new message" notifications.</summary>
        public List<Job> Notifies { get; } = [];

        public string Create(Job job, IState state)
        {
            (job.Type == typeof(ClinicCrmLeadJob) ? Leads
                : job.Type == typeof(ClinicCrmMessageJob) ? Notifies
                : Created).Add(job);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"inbound-media-{Guid.NewGuid()}")
                .Options,
            new Tenant());

    private static WabaConnection NewConnection(Guid companyId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PhoneNumberId = "pn-1",
        WabaId = "waba-1",
        DisplayPhoneNumber = "+15550100",
        EncryptedAccessToken = "token",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static WebhookContext ImageMessage(WabaConnection conn, string wamid, string from, string mediaId, string? caption) => new()
    {
        Field = "messages",
        Connection = conn,
        Change = new WebhookChange
        {
            Field = "messages",
            Value = new WebhookValue
            {
                Messages =
                [
                    new WebhookInboundMessage
                    {
                        From = from,
                        Id = wamid,
                        Timestamp = EpochSeconds(DateTime.UtcNow),
                        Type = "image",
                        Image = new WebhookMedia { Id = mediaId, MimeType = "image/jpeg", Caption = caption },
                    },
                ],
                Contacts = [new WebhookContact { WaId = from, Profile = new WebhookContactProfile { Name = "Mona" } }],
            },
        },
    };

    private static string EpochSeconds(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds().ToString();

    private static WebhookContext TextMessage(
        WabaConnection conn, string wamid, string from, string body, DateTime? sentAt = null) => new()
    {
        Field = "messages",
        Connection = conn,
        Change = new WebhookChange
        {
            Field = "messages",
            Value = new WebhookValue
            {
                Messages =
                [
                    new WebhookInboundMessage
                    {
                        From = from,
                        Id = wamid,
                        Timestamp = EpochSeconds(sentAt ?? DateTime.UtcNow),
                        Type = "text",
                        Text = new WebhookText { Body = body },
                    },
                ],
            },
        },
    };

    private static (InboundMessageWebhookHandler Handler, RecordingNotifier Notifier, RecordingJobClient Jobs, WebhookPostCommitActions PostCommit) NewHandler(
        AppDbContext db, IMemoryCache? cache = null, wa_api.Features.Bot.IBotGate? gate = null)
    {
        var notifier = new RecordingNotifier();
        var jobs = new RecordingJobClient();
        var postCommit = new WebhookPostCommitActions(NullLogger<WebhookPostCommitActions>.Instance);
        var handler = new InboundMessageWebhookHandler(db, notifier, postCommit, jobs,
            cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<InboundMessageWebhookHandler>.Instance,
            gate);
        return (handler, notifier, jobs, postCommit);
    }

    private sealed class TestClock : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task InboundImage_WithCaption_StoresMediaMetadata_AndEnqueuesDownloadJob()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, notifier, jobs, postCommit) = NewHandler(db);
        await handler.HandleAsync(ImageMessage(conn, "wamid-img-1", "15551234567", "media-abc", "look at this"));
        // Nothing is pushed before the commit — the inbox would refetch and cache the pre-commit state.
        Assert.Equal(0, notifier.Count);
        await db.SaveChangesAsync(); // mirrors the dispatcher's single atomic commit
        await postCommit.RunAsync();  // ...after which the job sends the queued pushes

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("image", msg.MediaType);
        Assert.Equal("image/jpeg", msg.MediaMimeType);
        Assert.Equal("media-abc", msg.MetaMediaId);
        Assert.Equal("look at this", msg.Body);      // caption wins over the "[image]" placeholder
        Assert.Null(msg.MediaDownloadedAt);           // bytes not fetched inline
        Assert.Equal(MessageDirection.Inbound, msg.Direction);

        // Two jobs are enqueued for THIS message id, neither run inline: the media download and the
        // chatbot. Order matters only in that both must reference the same message.
        Assert.Equal(2, jobs.Created.Count);

        var download = Assert.Single(jobs.Created, j => j.Type == typeof(InboundMediaDownloadJob));
        Assert.Equal(nameof(InboundMediaDownloadJob.RunAsync), download.Method.Name);
        Assert.Equal(msg.Id, Assert.IsType<Guid>(download.Args[0]));

        var autoReply = Assert.Single(jobs.Created, j => j.Type == typeof(AiReplyJob));
        Assert.Equal(nameof(AiReplyJob.RunAsync), autoReply.Method.Name);
        Assert.Equal(msg.Id, Assert.IsType<Guid>(autoReply.Args[0]));

        // The realtime push advertises media that isn't ready yet.
        Assert.Equal(1, notifier.Count);
        Assert.Equal("image", notifier.Last!.MediaType);
        Assert.False(notifier.Last!.MediaReady);
    }

    [Fact]
    public async Task AContact_IsQueuedToBeSavedAtTheClinic_WithTheirProfileName_AtMostOncePerGap()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var clock = new TestClock();
        var (handler, _, jobs, _) = NewHandler(db, new MemoryCache(new MemoryCacheOptions { Clock = clock }));
        await handler.HandleAsync(ImageMessage(conn, "wamid-lead-1", "971501234567", "media-1", null));
        await db.SaveChangesAsync();

        var lead = Assert.Single(jobs.Leads);
        Assert.Equal(nameof(ClinicCrmLeadJob.SaveAsync), lead.Method.Name);
        Assert.Equal(companyId, lead.Args[0]);
        Assert.Equal("971501234567", lead.Args[1]);
        Assert.Equal("Mona", lead.Args[2]);

        // Five minutes on, mid-chat: no second CRM call.
        clock.UtcNow += TimeSpan.FromMinutes(5);
        await handler.HandleAsync(TextMessage(conn, "wamid-lead-2", "971501234567", "hello again"));
        await db.SaveChangesAsync();
        Assert.Single(jobs.Leads);

        // Still chatting, but the gap since the last SAVE has passed: saved again, in case the clinic deleted
        // them meanwhile. (A gap measured from the last message would never re-save a chat that keeps going.)
        clock.UtcNow += InboundMessageWebhookHandler.ClinicSaveGap - TimeSpan.FromMinutes(4);
        await handler.HandleAsync(TextMessage(conn, "wamid-lead-3", "971501234567", "still here"));
        await db.SaveChangesAsync();
        Assert.Equal(2, jobs.Leads.Count);
        Assert.All(jobs.Leads, l => Assert.Equal("971501234567", l.Args[1]));
    }

    [Fact]
    public async Task ANewContact_WithoutAProfileName_IsQueuedWithNoName()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-lead-3", "971509999999", "hi"));

        Assert.Null(Assert.Single(jobs.Leads).Args[2]);
    }

    [Fact]
    public async Task InboundImage_NoCaption_UsesTypePlaceholderBody()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db);
        await handler.HandleAsync(ImageMessage(conn, "wamid-img-2", "15551234567", "media-xyz", caption: null));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("[image]", msg.Body);           // preview placeholder when no caption
        var convo = await db.Conversations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("[image]", convo.LastMessageBody);
        Assert.Equal(2, jobs.Created.Count);          // media download + auto-reply
    }

    [Fact]
    public async Task InboundText_StoresNoMedia_AndEnqueuesOnlyTheReplyJob()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-txt-1", "15551234567", "hello there"));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Null(msg.MediaType);
        Assert.Null(msg.MetaMediaId);
        Assert.Equal("hello there", msg.Body);
        // No media → no download job. The chatbot is still handed the message: AiReplyJob decides
        // for itself whether a rule matches, so the handler enqueues unconditionally.
        var job = Assert.Single(jobs.Created);
        Assert.Equal(typeof(AiReplyJob), job.Type);
        Assert.Equal(msg.Id, Assert.IsType<Guid>(job.Args[0]));
    }

    /// <summary>
    /// Staff get the handoff alert on WhatsApp, and a reply to it is a colleague talking. The bot answering a
    /// team member, or the clinic CRM saving them as a lead, would be the system talking to itself.
    /// </summary>
    [Fact]
    public async Task AMessageFromATeamNumber_IsStored_ButNeverWakesTheBot()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        db.BotProfiles.Add(new wa_api.Features.Bot.Entities.BotProfile
        {
            CompanyId = companyId, BusinessName = "Clinic", BusinessDescription = "A clinic.",
            HandoffAlertPhones = ["15551234567"],
        });
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-team-1", "15551234567", "on it"));
        await db.SaveChangesAsync();

        Assert.Equal("on it", (await db.Messages.IgnoreQueryFilters().SingleAsync()).Body);
        Assert.Empty(jobs.Created);
    }

    /// <summary>
    /// The production incident: the API was down overnight, Meta retried, and a "hi" sent at 9pm arrived
    /// at 3pm the next day. The bot answered it as if it were new. It must be stored but not answered, and
    /// the 24-hour window runs from when it was SENT, not when it arrived.
    /// </summary>
    [Fact]
    public async Task LateDelivery_IsStored_ButDoesNotWakeTheBot_AndWindowRunsFromSendTime()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var sentAt = DateTime.UtcNow.AddHours(-18);

        var (handler, notifier, jobs, postCommit) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-late-1", "15551234567", "hi", sentAt));
        await db.SaveChangesAsync();
        await postCommit.RunAsync();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("hi", msg.Body);                 // still in the inbox for a human
        Assert.Equal(1, notifier.Count);              // and still pushed live
        Assert.Empty(jobs.Created);                   // but no bot reply

        var convo = await db.Conversations.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(convo.WindowExpiresAt);
        var expected = sentAt.AddHours(24);
        Assert.InRange(convo.WindowExpiresAt!.Value, expected.AddSeconds(-1), expected.AddSeconds(1));
    }

    [Fact]
    public async Task RecentMessage_JustInsideTheCutoff_StillGetsABotReply()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db);
        await handler.HandleAsync(TextMessage(
            conn, "wamid-recent-1", "15551234567", "hello",
            DateTime.UtcNow - InboundMessageWebhookHandler.MaxBotReplyAge + TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        Assert.Equal(typeof(AiReplyJob), Assert.Single(jobs.Created).Type);
    }

    /// <summary>A stale message arriving after a newer one must not shrink the window the newer one opened.</summary>
    [Fact]
    public async Task LateDelivery_AfterANewerMessage_DoesNotShortenTheWindow()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, _, _) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-new", "15551234567", "fresh"));
        await db.SaveChangesAsync();

        var windowAfterFresh = (await db.Conversations.IgnoreQueryFilters().SingleAsync()).WindowExpiresAt;

        await handler.HandleAsync(TextMessage(
            conn, "wamid-old", "15551234567", "stale", DateTime.UtcNow.AddHours(-18)));
        await db.SaveChangesAsync();

        var convo = await db.Conversations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(windowAfterFresh, convo.WindowExpiresAt);
    }

    // ── CRM integration (plan 005): a company without the chatbot costs no extra jobs ──────────────

    [Fact]
    public async Task ChatbotNotLive_TheMessageIsStored_ButNoBotOrClinicJobIsEnqueued()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db, gate: new wa_api.Tests.Bot.TestBotGate(live: false, clinic: false));
        await handler.HandleAsync(TextMessage(conn, "wamid-nobot-1", "15551234567", "hello"));
        await db.SaveChangesAsync();

        Assert.Single(await db.Messages.IgnoreQueryFilters().ToListAsync());   // still lands in the inbox
        Assert.Empty(jobs.Created);
    }

    [Fact]
    public async Task ChatbotLive_WithoutAClinicConnection_EnqueuesOnlyTheReplyJob()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db, gate: new wa_api.Tests.Bot.TestBotGate(live: true, clinic: false));
        await handler.HandleAsync(TextMessage(conn, "wamid-bot-1", "15551234567", "hello"));
        await db.SaveChangesAsync();

        Assert.Equal(typeof(AiReplyJob), Assert.Single(jobs.Created).Type);
    }

    // ── Clinic CRM "new message" notification ────────────────────────────────────────────────────

    private static wa_api.Tests.Bot.TestBotGate NotifyGate(bool notify = true) =>
        new(live: true, clinic: true) { ClinicNotify = notify };

    [Fact]
    public async Task NotifyOn_AMeaningfulMessage_QueuesOneNotification_AtMostOncePerGap()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var clock = new TestClock();
        var (handler, _, jobs, _) = NewHandler(db, new MemoryCache(new MemoryCacheOptions { Clock = clock }), NotifyGate());
        await handler.HandleAsync(TextMessage(conn, "wamid-n-1", "971501234567", "Do you have a slot tomorrow?"));
        await db.SaveChangesAsync();

        var notify = Assert.Single(jobs.Notifies);
        Assert.Equal(nameof(ClinicCrmMessageJob.NotifyAsync), notify.Method.Name);
        Assert.Equal(companyId, notify.Args[0]);
        Assert.Equal("971501234567", notify.Args[1]);

        // Second line of the same question, a minute later: still one notification.
        clock.UtcNow += TimeSpan.FromMinutes(1);
        await handler.HandleAsync(TextMessage(conn, "wamid-n-2", "971501234567", "Around 4pm"));
        await db.SaveChangesAsync();
        Assert.Single(jobs.Notifies);

        // Past the gap, a new message notifies again.
        clock.UtcNow += InboundMessageWebhookHandler.ClinicNotifyGap;
        await handler.HandleAsync(TextMessage(conn, "wamid-n-3", "971501234567", "Is that possible?"));
        await db.SaveChangesAsync();
        Assert.Equal(2, jobs.Notifies.Count);
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData("ok 👍")]
    [InlineData("Thanks!!")]
    [InlineData("🙏🙏")]
    public async Task NotifyOn_StopOrAnAcknowledgement_IsNotSentToTheClinic(string body)
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db, gate: NotifyGate());
        await handler.HandleAsync(TextMessage(conn, "wamid-skip-1", "971501234567", body));
        await db.SaveChangesAsync();

        Assert.Single(await db.Messages.IgnoreQueryFilters().ToListAsync());   // still in the inbox
        Assert.Empty(jobs.Notifies);
    }

    [Fact]
    public async Task NotifyOff_NothingIsSentToTheClinic_EvenForARealQuestion()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs, _) = NewHandler(db, gate: NotifyGate(notify: false));
        await handler.HandleAsync(TextMessage(conn, "wamid-off-1", "971501234567", "How much is a PRP session?"));
        await db.SaveChangesAsync();

        Assert.Empty(jobs.Notifies);
        Assert.Single(jobs.Leads);   // the lead save is unaffected by this switch
    }
}
