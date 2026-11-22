using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Locking;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins the guards in <see cref="AiReplyJob"/>. It inherits every guard from <c>AutoReplyJob</c> plus a
/// bot-paused one, and it now spends money per reply — so "when does the bot NOT speak" matters more
/// here than in the rule engine, and each precondition is broken individually.
/// <para>
/// The planner is faked throughout. What Claude actually says is not testable and is not the point;
/// what IS testable is whether the job ever reaches the planner, and these tests are about that.
/// </para>
/// </summary>
public class AiReplyJobTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private const string InboundWamid = "wamid.inbound-1";

    /// <summary>A job's scope starts with no tenant, exactly as Hangfire hands it over.</summary>
    private static ScopedTenantContext NewTenant() => new(new HttpContextAccessor());

    private sealed class RecordingSender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<(string Body, bool ChargeCredit)> Sent { get; } = [];

        /// <summary>Wamids the customer-facing typing indicator was shown for.</summary>
        public List<string?> Typing { get; } = [];

        /// <summary>Sends and typing calls in the order they happened: "typing" or the bubble text.</summary>
        public List<string> Timeline { get; } = [];

        public bool ThrowOnTyping { get; set; }

        public Task ShowTypingAsync(WabaConnection waba, string? inboundWamid, CancellationToken ct = default)
        {
            if (ThrowOnTyping) throw new HttpRequestException("Meta is down.");
            Typing.Add(inboundWamid);
            Timeline.Add("typing");
            return Task.CompletedTask;
        }

        public async Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            Sent.Add((body, chargeCredit));
            Timeline.Add(body);

            // Mirrors the real sender: it commits the row itself, BEFORE the caller can stamp
            // IsAutoReply. A fake that returned a detached object would hide the retry window the
            // direction-keyed idempotency guard exists to close.
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

    /// <summary>Records whether the model was reached at all, and answers with a fixed reply.</summary>
    private sealed class FakePlanner(bool configured = true, string? reply = "Sure, we do that.\n\nWant a time?")
        : IAiReplyPlanner
    {
        public int Calls { get; private set; }
        public AiTurnContext? LastContext { get; private set; }
        public string? Error { get; set; }
        public string? ToolCalled { get; set; }
        public bool Booked { get; set; }
        public List<Guid> SharedLinkIds { get; set; } = [];

        /// <summary>Runs inside the model call, with its number (1-based): what happens "while the model thinks".</summary>
        public Func<int, Task>? OnPlan { get; set; }

        public bool IsConfigured => configured;

        public string Model => "claude-haiku-4-5";

        public async Task<AiReplyPlan> PlanAsync(
            AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default)
        {
            Calls++;
            LastContext = ctx;

            if (OnPlan is not null) await OnPlan(Calls);

            if (Error is not null) return AiReplyPlan.Failed(Error);

            AiToolInvocation[] calls = ToolCalled is null ? [] : [new(ToolCalled, "{}", "ok")];

            return new AiReplyPlan(
                BubbleSplitter.Split(reply), calls, false, Booked, 100, 20, 0, 0, null, SharedLinkIds);
        }
    }

    private sealed class StubAvailability : IAvailabilityService
    {
        public Task<AvailabilityResponse> GetAsync(
            Guid appointmentTypeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
            => Task.FromResult(new AvailabilityResponse("UTC", default, default, []));

        public Task<bool> IsOfferableAsync(
            Guid appointmentTypeId, DateTime startUtc, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class StubAppointments : IAppointmentService
    {
        public Task<AppointmentResponse> CreateMirrorAsync(
            CreateAppointmentRequest r, DateTime? endUtc, ExternalBookingLink link, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> ApplyExternalMoveAsync(
            Guid id, DateTime startUtc, DateTime? endUtc, int externalBookingId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AppointmentResponse>> GetRangeAsync(
            DateTime fromUtc, DateTime toUtc, bool includeCancelled = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AppointmentResponse>>([]);

        public Task<AppointmentResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> CreateAsync(
            CreateAppointmentRequest request, AppointmentSource source, Guid? createdByUserId,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AppointmentResponse> RescheduleAsync(
            Guid id, RescheduleAppointmentRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> CancelAsync(
            Guid id, CancelAppointmentRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> UpdateStatusAsync(
            Guid id, UpdateAppointmentStatusRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static AppDbContext NewDb(ITenantContext tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"ai-reply-{Guid.NewGuid()}")
                .Options,
            tenant);

    /// <summary>
    /// Seeds a healthy world: an active WABA, an opted-in contact, an open conversation inside its
    /// 24-hour window, one inbound message, and an ENABLED bot profile. No keyword rules, so the AI path
    /// is the one under test. Each test then breaks exactly one precondition.
    /// </summary>
    private static async Task<Fixture> SeedAsync(
        bool withProfile = true, bool profileEnabled = true, int dailyLimit = 500)
    {
        var tenant = NewTenant();
        var db = NewDb(tenant);
        var now = DateTime.UtcNow;

        var company = new wa_api.Features.Companies.Company
        {
            Id = CompanyId,
            Name = $"Test Co {Guid.NewGuid()}",
            TimeZoneId = "UTC",
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

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
            Name = "Mona Perera",
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
            BotState = ConversationBotState.Active,  // bot is running this thread
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
            Body = "do you do teeth cleaning?",
            ExternalMessageId = InboundWamid,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
        };

        db.Companies.Add(company);
        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.Messages.Add(inbound);

        if (withProfile)
        {
            db.BotProfiles.Add(new BotProfile
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyId,
                IsEnabled = profileEnabled,
                BusinessName = "Colombo Dental",
                BusinessDescription = "A family dental clinic.",
                DailyReplyLimit = dailyLimit,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        return new Fixture(db, tenant, inbound, conversation, contact);
    }

    private sealed record Fixture(
        AppDbContext Db, ScopedTenantContext Tenant, Message Inbound, Conversation Conversation, Contact Contact);

    /// <summary>Records what the job scheduled, so the follow-up can be asserted without Hangfire.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        public List<IState> States { get; } = [];
        public string Create(Job job, IState state)
        {
            Created.Add(job);
            States.Add(state);
            return Guid.NewGuid().ToString();
        }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    /// <summary>The conversation lock: free, held by another job, or doing something as it is taken.</summary>
    private sealed class FakeLocks(bool busy = false) : IDistributedLockService
    {
        public List<string> Requested { get; } = [];
        public int Released { get; private set; }
        public Func<Task>? OnAcquire { get; set; }

        public async Task<IAsyncDisposable?> AcquireAsync(string resource, CancellationToken ct = default)
        {
            Requested.Add(resource);
            if (busy) return null;
            if (OnAcquire is not null) await OnAcquire();
            return new Handle(this);
        }

        private sealed class Handle(FakeLocks owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>Another message from the same customer, <paramref name="secondsLater"/> after the seeded one.</summary>
    private static async Task<Message> AddMessageAsync(
        Fixture f, string body, double secondsLater, MessageDirection direction = MessageDirection.Inbound)
    {
        var at = f.Inbound.CreatedAt.AddSeconds(secondsLater);
        var message = new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = f.Contact.Id,
            WabaConnectionId = f.Inbound.WabaConnectionId,
            ConversationId = f.Conversation.Id,
            Body = body,
            ExternalMessageId = $"wamid-{Guid.NewGuid()}",
            Direction = direction,
            Status = direction == MessageDirection.Inbound ? MessageStatus.Delivered : MessageStatus.Sent,
            CreatedAt = at,
            UpdatedAt = at,
            IsActive = true,
        };

        f.Db.Messages.Add(message);
        await f.Db.SaveChangesAsync();
        return message;
    }

    private static (AiReplyJob Job, RecordingSender Sender) NewJob(
        Fixture f, IAiReplyPlanner planner, RecordingJobClient? jobClient = null,
        IDistributedLockService? locks = null, wa_api.Features.Bot.IBotGate? gate = null)
    {
        var jobs = jobClient ?? new RecordingJobClient();
        var sender = new RecordingSender(f.Db);
        var job = new AiReplyJob(
            f.Db,
            f.Tenant,
            new AutoReplyEngine(NullLogger<AutoReplyEngine>.Instance),
            planner,
            new StubAvailability(),
            new StubAppointments(),
            sender,
            new NoopNotifier(),
            jobs,
            TestAiUsage.Logger(f.Db),
            NullLogger<BotToolExecutor>.Instance,
            NullLogger<AiReplyJob>.Instance,
            locks: locks,
            gate: gate);

        return (job, sender);
    }

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMatchingRule_ReachesTheModel_AndSendsItsBubbles()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Equal(2, sender.Sent.Count);                     // two bubbles, two sends
        Assert.Equal("Sure, we do that.", sender.Sent[0].Body);
        Assert.Equal("Want a time?", sender.Sent[1].Body);
    }

    [Fact]
    public async Task AConfiguredSequence_SchedulesItsFirstStep()
    {
        var f = await SeedAsync();
        var profile = await f.Db.BotProfiles.IgnoreQueryFilters().FirstAsync();
        profile.FollowUpSteps = [new BotFollowUpStep { DelayMinutes = 360, Message = "hold a slot?" }];
        await f.Db.SaveChangesAsync();

        var jobs = new RecordingJobClient();
        var (job, _) = NewJob(f, new FakePlanner(), jobs);

        var before = DateTime.UtcNow;
        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var scheduled = Assert.IsType<ScheduledState>(Assert.Single(jobs.States));
        Assert.InRange((scheduled.EnqueueAt - before).TotalMinutes, 359, 361);
        Assert.Equal(nameof(BotFollowUpJob.RunStepAsync), Assert.Single(jobs.Created).Method.Name);
    }

    [Fact]
    public async Task InWindowReplies_AreNeverCharged()
    {
        // Guard 4 already proved the window is open, and Meta bills the whole window as one
        // conversation — so charging per bubble would bill the tenant for nothing.
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.All(sender.Sent, s => Assert.False(s.ChargeCredit));
    }

    [Fact]
    public async Task EveryBubble_IsStampedAsAnAutoReply()
    {
        // The loop breaker depends on this: an unstamped reply would be answered by the next run.
        var f = await SeedAsync();
        var (job, _) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var outbound = await f.Db.Messages.IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound)
            .ToListAsync();

        Assert.NotEmpty(outbound);
        Assert.All(outbound, m => Assert.True(m.IsAutoReply));
    }

    [Fact]
    public async Task TheModelIsGivenTheBusinessAndTheThread()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var ctx = Assert.IsType<AiTurnContext>(planner.LastContext);
        Assert.Equal("Colombo Dental", ctx.Business.BusinessName);
        Assert.Equal("Mona", ctx.ContactFirstName);                    // first name only
        Assert.Equal("do you do teeth cleaning?", ctx.Transcript[^1].Text);
        Assert.True(ctx.Transcript[^1].IsCustomer);
    }

    // ── The guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PausedConversation_StaysSilent()
    {
        // The reason the AI is safe to leave switched on: it does not talk over the agent who stepped in.
        var f = await SeedAsync();
        f.Conversation.BotState = ConversationBotState.Paused;
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OptedOutContact_StaysSilent()
    {
        // Replying after a STOP is a Meta policy violation, not just bad manners.
        var f = await SeedAsync();
        f.Contact.IsOptedOut = true;
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ClosedWindow_StaysSilent()
    {
        var f = await SeedAsync();
        f.Conversation.WindowExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OutboundMessage_IsNeverAnswered()
    {
        var f = await SeedAsync();
        f.Inbound.Direction = MessageDirection.Outbound;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OurOwnAutoReply_IsNeverAnswered()
    {
        // The loop breaker.
        var f = await SeedAsync();
        f.Inbound.IsAutoReply = true;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AHumanWhoAlreadyReplied_IsNotTalkedOver()
    {
        // The direction-keyed idempotency guard, in its second role.
        var f = await SeedAsync();
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId,
            ConversationId = f.Conversation.Id,
            Body = "I've got this one",
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            CreatedAt = f.Inbound.CreatedAt.AddSeconds(5),
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AFollowUpSentSecondsAfterOurReply_IsStillAnswered()
    {
        // Regression: a 60-second cooldown copied from AutoReplyJob silently dropped this. Observed
        // live — the bot answered "Hi", the customer replied 31 seconds later describing why they were
        // messaging, and got nothing back. Replying inside a minute is what a conversation IS.
        var f = await SeedAsync();
        AddOutbound(f, "Hi Pasindu! How can I help you today?",
            at: DateTime.UtcNow.AddSeconds(-31), isAutoReply: true);

        // The customer's new message arrives AFTER our reply — the case that must be answered.
        f.Inbound.CreatedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.NotEmpty(sender.Sent);
    }

    [Fact]
    public async Task ABurstOfMessages_StillGetsOnlyOneReply()
    {
        // The behaviour the cooldown was there for, now carried by the idempotency guard: when we have
        // already replied at or after this message, it was covered by that reply.
        var f = await SeedAsync();
        AddOutbound(f, "Hi! How can I help?",
            at: f.Inbound.CreatedAt.AddSeconds(2), isAutoReply: true);
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    private static void AddOutbound(Fixture f, string body, DateTime at, bool isAutoReply)
    {
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId,
            ConversationId = f.Conversation.Id,
            Body = body,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            IsAutoReply = isAutoReply,
            CreatedAt = at,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
    }

    // ── The conversion attempt counter ────────────────────────────────────────

    [Fact]
    public async Task OfferingTimes_IncrementsTheStoredAttemptCount()
    {
        // The limit has to be a fact on the row. Counting from the transcript stopped working past
        // twenty messages, when the earlier asks fell out of context.
        var f = await SeedAsync();
        var planner = new FakePlanner { ToolCalled = "check_availability" };
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var conversation = await f.Db.Conversations.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(1, conversation.BotAsksMade);
        Assert.Null(conversation.BotConvertedAt);
    }

    [Fact]
    public async Task AReplyThatOffersNothing_DoesNotBurnAnAttempt()
    {
        // Answering "what time do you close?" must not cost a sales attempt.
        var f = await SeedAsync();
        var (job, _) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, (await f.Db.Conversations.IgnoreQueryFilters().FirstAsync()).BotAsksMade);
    }

    [Fact]
    public async Task ABooking_StopsTheSellingAndResetsTheCount()
    {
        // Booked is the goal, so the counter starts clean for whatever they come back about later.
        var f = await SeedAsync();
        f.Conversation.BotAsksMade = 2;
        await f.Db.SaveChangesAsync();

        var (job, _) = NewJob(f, new FakePlanner { Booked = true });

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var conversation = await f.Db.Conversations.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(0, conversation.BotAsksMade);
        Assert.NotNull(conversation.BotConvertedAt);
    }

    [Fact]
    public async Task TheStoredCount_ReachesTheModel()
    {
        var f = await SeedAsync();
        f.Conversation.BotAsksMade = 2;
        await f.Db.SaveChangesAsync();
        await AddAppointmentAsync(f, DateTime.UtcNow.AddDays(2), AppointmentStatus.Confirmed);

        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(2, planner.LastContext!.AsksMade);
        Assert.True(planner.LastContext!.AlreadyBooked);
        var booking = Assert.Single(planner.LastContext!.Bookings!);
        Assert.True(booking.IsUpcoming);
    }

    /// <summary>
    /// The production incident: booked by the bot on 11 Sep, back on 17 Sep asking for treatment, told
    /// "you've already got an appointment" — then, one message later, "you don't have one". The stamp from
    /// the old booking must not read as booked once that appointment has gone by.
    /// </summary>
    [Fact]
    public async Task APastBooking_IsNotBooked_ButIsShownAsHistory()
    {
        var f = await SeedAsync();
        f.Conversation.BotConvertedAt = DateTime.UtcNow.AddDays(-6);
        await f.Db.SaveChangesAsync();
        await AddAppointmentAsync(f, DateTime.UtcNow.AddDays(-6), AppointmentStatus.Confirmed);
        await AddAppointmentAsync(f, DateTime.UtcNow.AddDays(-3), AppointmentStatus.Cancelled);

        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.False(planner.LastContext!.AlreadyBooked);
        var bookings = planner.LastContext!.Bookings!;
        Assert.Equal(2, bookings.Count);
        Assert.All(bookings, b => Assert.False(b.IsUpcoming));
        Assert.Equal(AppointmentStatus.Cancelled, bookings[0].Status); // newest first
    }

    private static async Task AddAppointmentAsync(Fixture f, DateTime startUtc, AppointmentStatus status)
    {
        var now = DateTime.UtcNow;
        var type = new AppointmentType
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Name = "Consultation", DurationMinutes = 30,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        f.Db.AppointmentTypes.Add(type);
        f.Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id, AppointmentTypeId = type.Id,
            ConversationId = f.Conversation.Id, StartUtc = startUtc, EndUtc = startUtc.AddMinutes(30),
            BlockStartUtc = startUtc, BlockEndUtc = startUtc.AddMinutes(30), Status = status,
            Source = AppointmentSource.Bot, CreatedAt = now, UpdatedAt = now, IsActive = true,
        });
        await f.Db.SaveChangesAsync();
    }

    // ── Configuration gates ───────────────────────────────────────────────────

    [Fact]
    public async Task NoApiKey_SkipsTheAiPathQuietly()
    {
        // A missing key is a supported state, not an outage.
        var f = await SeedAsync();
        var planner = new FakePlanner(configured: false);
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task NoBotProfile_SkipsTheAiPath()
    {
        var f = await SeedAsync(withProfile: false);
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DisabledProfile_SkipsTheAiPath()
    {
        var f = await SeedAsync(profileEnabled: false);
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DailyLimitReached_StaysSilentAndIsRecorded()
    {
        // A per-message LLM with no ceiling is an unbounded bill the first time a tenant is spammed.
        var f = await SeedAsync(dailyLimit: 1);
        f.Db.AiReplyLogs.Add(new AiReplyLog
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ConversationId = f.Conversation.Id,
            InboundMessageId = Guid.NewGuid(),
            Model = "claude-haiku-4-5",
            Outcome = AiReplyOutcome.Replied,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
        Assert.Contains(
            await f.Db.AiReplyLogs.IgnoreQueryFilters().ToListAsync(),
            l => l.Outcome == AiReplyOutcome.BudgetExceeded);
    }

    [Fact]
    public async Task DailyLimit_CountsRepliesOnly_NotFollowUpsTemplatesOrPreviews()
    {
        // Plan 013 logs every Anthropic call for cost, but the limit was always about answering customers.
        var f = await SeedAsync(dailyLimit: 1);
        foreach (var kind in new[] { AiCallKind.FollowUp, AiCallKind.TemplateVariables, AiCallKind.Preview })
        {
            f.Db.AiReplyLogs.Add(new AiReplyLog
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyId,
                Kind = kind,
                ConversationId = kind == AiCallKind.Preview ? null : f.Conversation.Id,
                Model = "claude-haiku-4-5",
                Outcome = AiReplyOutcome.Replied,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
            });
        }
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.NotEmpty(sender.Sent);
    }

    [Fact]
    public async Task AReply_IsLoggedAsAReplyWithItsCost()
    {
        var f = await SeedAsync();
        var (job, _) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var log = Assert.Single(await f.Db.AiReplyLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AiCallKind.Reply, log.Kind);
        Assert.Equal(f.Inbound.Id, log.InboundMessageId);
        // FakePlanner: 100 input + 20 output on Haiku 4.5 = 100×$1/M + 20×$5/M.
        Assert.Equal(0.0002m, log.CostUsd);
    }

    // ── Failure handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task AFailedModelCall_SendsNothingWhenThereIsNoFallbackRule()
    {
        // Silence beats a half-written message on a customer's phone.
        var f = await SeedAsync();
        var planner = new FakePlanner { Error = "Anthropic request timed out." };
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Contains(
            await f.Db.AiReplyLogs.IgnoreQueryFilters().ToListAsync(),
            l => l.Outcome == AiReplyOutcome.Failed);
    }

    [Fact]
    public async Task WithTheAssistantOn_ACatchAllRuleDoesNotSwallowEveryMessage()
    {
        // Without the demotion, a Fallback rule matches everything and the AI would never run for any
        // tenant who has written one — which is most of them.
        var f = await SeedAsync();
        AddCatchAllRule(f);

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Equal("Sure, we do that.", sender.Sent[0].Body);
    }

    [Fact]
    public async Task AFailedModelCall_FallsBackToTheTenantsCatchAllRule()
    {
        // The catch-all's real job once the assistant is on: the safety net when Claude is unreachable.
        var f = await SeedAsync();
        AddCatchAllRule(f);

        var planner = new FakePlanner { Error = "Rate limited by Anthropic." };
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Equal("Thanks! We'll get back to you shortly.", Assert.Single(sender.Sent).Body);
    }

    [Fact]
    public async Task WithTheAssistantOff_ACatchAllRuleStillAnswersEverything()
    {
        // The no-regression case: nothing about today's behaviour changes for a tenant who has not
        // switched the assistant on.
        var f = await SeedAsync(profileEnabled: false);
        AddCatchAllRule(f);

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Equal("Thanks! We'll get back to you shortly.", Assert.Single(sender.Sent).Body);
    }

    [Fact]
    public async Task AContentRule_StillWinsOutrightOverTheAssistant()
    {
        // "Rules first" is for saying a specific thing precisely — that must keep working.
        var f = await SeedAsync();
        f.Db.AutoReplyRules.Add(new AutoReplyRule
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            Name = "cleaning price",
            Trigger = AutoReplyTrigger.Keyword,
            Terms = ["cleaning"],
            ReplyBody = "Cleaning is Rs. 3500.",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Equal("Cleaning is Rs. 3500.", Assert.Single(sender.Sent).Body);
    }

    // ── The customer's typing indicator ───────────────────────────────────────

    [Fact]
    public async Task AnAiReply_ShowsTyping_ForTheInboundMessage_BeforeEachBubble()
    {
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        // Shown before the first bubble, and again before the second — sending bubble one cleared it.
        Assert.Equal(["typing", "Sure, we do that.", "typing", "Want a time?"], sender.Timeline);
        Assert.All(sender.Typing, w => Assert.Equal(InboundWamid, w));
    }

    [Fact]
    public async Task ASingleBubbleReply_ShowsTypingOnce()
    {
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner(reply: "Yes, we do."));

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(["typing", "Yes, we do."], sender.Timeline);
    }

    [Fact]
    public async Task APausedConversation_NeverShowsTyping()
    {
        // Meta's rule: only show it when a reply is coming. The guards must run first.
        var f = await SeedAsync();
        f.Conversation.BotState = ConversationBotState.Paused;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());
        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Typing);
    }

    [Fact]
    public async Task AClosedWindow_NeverShowsTyping()
    {
        var f = await SeedAsync();
        f.Conversation.WindowExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());
        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Typing);
    }

    [Fact]
    public async Task AKeywordRuleReply_DoesNotShowTyping()
    {
        // A canned rule reply goes out instantly; there is no composing to show.
        var f = await SeedAsync();
        f.Db.AutoReplyRules.Add(new AutoReplyRule
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            Name = "cleaning price",
            Trigger = AutoReplyTrigger.Keyword,
            Terms = ["cleaning"],
            ReplyBody = "Cleaning is Rs. 3500.",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());
        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Typing);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task ATypingIndicatorFailure_StillSendsTheReply()
    {
        // Cosmetic. It must never cost the customer their answer.
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner());
        sender.ThrowOnTyping = true;

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task NoInboundWamid_SkipsTyping_ButStillReplies()
    {
        var f = await SeedAsync();
        f.Inbound.ExternalMessageId = null;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());
        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Typing);
        Assert.Equal(2, sender.Sent.Count);
    }

    private static void AddCatchAllRule(Fixture f)
    {
        f.Db.AutoReplyRules.Add(new AutoReplyRule
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            Name = "catch-all",
            Trigger = AutoReplyTrigger.Fallback,
            ReplyBody = "Thanks! We'll get back to you shortly.",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        });
        f.Db.SaveChanges();
    }

    [Fact]
    public async Task AnEmptyPlan_SendsNothingAndIsRecorded()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner(reply: "   ");
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Contains(
            await f.Db.AiReplyLogs.IgnoreQueryFilters().ToListAsync(),
            l => l.Outcome == AiReplyOutcome.NoReply);
    }

    // ── Shared links ──────────────────────────────────────────────────────────

    private static BotLink AddLink(Fixture f, string title, string url, bool enabled = true, string? message = null)
    {
        var link = new BotLink
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            Title = title,
            Url = url,
            Message = message,
            IsEnabled = enabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        };
        f.Db.BotLinks.Add(link);
        f.Db.SaveChanges();
        return link;
    }

    [Fact]
    public async Task ASharedLink_IsSentAfterTheReply_WithTheStoredUrl()
    {
        // The model only names the link; the URL the customer gets is the one the owner saved.
        var f = await SeedAsync();
        var link = AddLink(f, "Before & after photos", "https://example.com/results",
            message: "Here are some real results 👇");

        var planner = new FakePlanner(reply: "Yes, of course!") { SharedLinkIds = [link.Id] };
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal("Yes, of course!", sender.Sent[0].Body);
        Assert.Equal("Here are some real results 👇\nhttps://example.com/results", sender.Sent[1].Body);
    }

    [Fact]
    public async Task ALinkDisabledBeforeSending_IsNotSent()
    {
        var f = await SeedAsync();
        var link = AddLink(f, "Price list", "https://example.com/prices", enabled: false);

        var planner = new FakePlanner(reply: "Here you go.") { SharedLinkIds = [link.Id] };
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal("Here you go.", Assert.Single(sender.Sent).Body);
    }

    [Fact]
    public async Task TheModelIsShownLinkTitles()
    {
        var f = await SeedAsync();
        AddLink(f, "Before & after photos", "https://example.com/results");

        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var links = planner.LastContext!.Business.Links!;
        Assert.Equal("Before & after photos", Assert.Single(links).Title);
    }

    // ── A burst of messages gets one turn, not one each ───────────────────────

    [Fact]
    public async Task AnOlderMessageOfABurst_LeavesTheReplyToTheNewestOne()
    {
        // "Book Tuesday" / "10am" / "PRP" used to be three turns that each booked 10am.
        var f = await SeedAsync();
        var newest = await AddMessageAsync(f, "10am", secondsLater: 1);

        var planner = new FakePlanner();
        var (older, olderSender) = NewJob(f, planner);
        await older.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(olderSender.Sent);

        var (job, sender) = NewJob(f, planner);
        await job.RunAsync(newest.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task AConversationAnotherJobIsAnswering_IsTriedAgainShortly_NotAnsweredAlongside()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var jobs = new RecordingJobClient();
        var locks = new FakeLocks(busy: true);
        var (job, sender) = NewJob(f, planner, jobs, locks);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
        Assert.Contains(f.Conversation.Id.ToString(), Assert.Single(locks.Requested));

        var retry = Assert.Single(jobs.Created);
        Assert.Equal(typeof(AiReplyJob), retry.Type);
        Assert.Equal(f.Inbound.Id, retry.Args[0]);
        var scheduled = Assert.IsType<ScheduledState>(Assert.Single(jobs.States));
        Assert.InRange(scheduled.EnqueueAt - DateTime.UtcNow, TimeSpan.Zero, AiReplyJob.BusyRetryDelay);
    }

    [Fact]
    public async Task TheConversationLock_IsReleasedAfterTheTurn()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var locks = new FakeLocks();
        var (job, sender) = NewJob(f, planner, locks: locks);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal(1, locks.Released);
    }

    [Fact]
    public async Task AMessageAnsweredWhileWaitingForTheConversation_IsNotAnsweredTwice()
    {
        // The job that held the conversation replied between this job's first look and its turn.
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var locks = new FakeLocks
        {
            OnAcquire = () => AddMessageAsync(f, "You're all set.", secondsLater: 2, MessageDirection.Outbound),
        };
        var (job, sender) = NewJob(f, planner, locks: locks);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
        Assert.Equal(1, locks.Released);
    }

    [Fact]
    public async Task AMessageArrivingMidTurn_DropsTheStaleReply_AndTheTurnIsTakenAgain()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        planner.OnPlan = call => call == 1 ? AddMessageAsync(f, "actually, 10am", secondsLater: 1) : Task.CompletedTask;
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        // Two model calls, one reply: the first answered a question the customer had moved on from.
        Assert.Equal(2, planner.Calls);
        Assert.Equal(2, sender.Sent.Count);
        Assert.Contains(planner.LastContext!.Transcript, t => t.Text == "actually, 10am");

        var logs = await f.Db.AiReplyLogs.IgnoreQueryFilters().OrderBy(l => l.CreatedAt).ToListAsync();
        Assert.Contains(logs, l => l.Outcome == AiReplyOutcome.NoReply && l.Error == "Superseded by a newer message.");
        Assert.Contains(logs, l => l.Outcome == AiReplyOutcome.Replied);
    }

    [Fact]
    public async Task AMessageArrivingMidTurn_AfterTheTurnChangedSomething_IsAnsweredInItsOwnTurn()
    {
        // A cancellation cannot be thrown away, so the customer is told — and then their newer message
        // is answered too, rather than lost behind the reply that never saw it.
        var f = await SeedAsync();
        var planner = new FakePlanner { ToolCalled = BotTools.CancelAppointment };
        planner.OnPlan = call => call == 1 ? AddMessageAsync(f, "and book me Friday", secondsLater: 1) : Task.CompletedTask;
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(2, planner.Calls);
        Assert.Equal(4, sender.Sent.Count);
    }

    [Fact]
    public async Task ACustomerWhoKeepsTyping_DoesNotKeepOneJobBusyForever()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        planner.OnPlan = call => AddMessageAsync(f, $"and another thing {call}", secondsLater: call);
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(3, planner.Calls);
    }

    [Fact]
    public async Task AMissingMessage_ThrowsSoHangfireRetriesPastTheCommitRace()
    {
        // The webhook enqueues before its batch commits, so a miss is a race to retry past — not a
        // message that does not exist.
        var f = await SeedAsync();
        var (job, _) = NewJob(f, new FakePlanner());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.RunAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ── CRM integration (plan 005): the gate, the stale guard and campaign context ──────────────

    [Fact]
    public async Task ChatbotNotLiveForTheCompany_TheModelIsNeverCalled_AndNothingIsSent()
    {
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner, gate: new TestBotGate(live: false));

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AMessageThatWaitedTooLongForAWorker_IsLeftForAPerson()
    {
        var f = await SeedAsync();
        var gate = new TestBotGate();
        gate.Options.MaxReplyAgeMinutes = 15;
        f.Inbound.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
        await f.Db.SaveChangesAsync();
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner, gate: gate);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AFreshMessage_IsStillAnswered_WhenTheGateIsLive()
    {
        var f = await SeedAsync();
        var gate = new TestBotGate();
        gate.Options.MaxReplyAgeMinutes = 15;
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner, gate: gate);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.NotEmpty(sender.Sent);
    }

    /// <summary>A campaign send: outbound, tied to a campaign, and — as CampaignBatchSendJob writes it — to no conversation.</summary>
    private static async Task AddCampaignMessageAsync(
        Fixture f, string body, Guid contactId, double hoursBeforeInbound, MessageStatus status = MessageStatus.Delivered)
    {
        var at = f.Inbound.CreatedAt.AddHours(-hoursBeforeInbound);
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ContactId = contactId,
            WabaConnectionId = f.Inbound.WabaConnectionId,
            ConversationId = null,
            CampaignId = Guid.NewGuid(),
            CampaignRunNumber = 1,
            Body = body,
            ExternalMessageId = $"wamid-{Guid.NewGuid()}",
            Direction = MessageDirection.Outbound,
            Status = status,
            CreatedAt = at,
            UpdatedAt = at,
            IsActive = true,
        });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ACampaignSentToThisCustomer_IsShownToTheModel_BeforeTheirReply()
    {
        var f = await SeedAsync();
        await AddCampaignMessageAsync(f, "Eid offer: 30% off teeth whitening this week.", f.Contact.Id, hoursBeforeInbound: 2);
        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        var transcript = planner.LastContext!.Transcript;
        var campaign = Assert.Single(transcript, t => t.Text.Contains("30% off teeth whitening"));
        Assert.False(campaign.IsCustomer);                                  // something the business said
        Assert.True(transcript[^1].IsCustomer);                             // and the customer's reply comes after it
        Assert.Same(campaign, transcript[0]);
    }

    [Fact]
    public async Task ACampaignSentToSomeoneElse_OrLongAgo_OrThatFailed_IsNotShown()
    {
        var f = await SeedAsync();
        await AddCampaignMessageAsync(f, "OTHER CUSTOMER", Guid.NewGuid(), hoursBeforeInbound: 2);
        await AddCampaignMessageAsync(f, "LAST MONTH", f.Contact.Id, hoursBeforeInbound: 24 * 30);
        await AddCampaignMessageAsync(f, "NEVER ARRIVED", f.Contact.Id, hoursBeforeInbound: 1, MessageStatus.Failed);
        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Inbound.Id, CancellationToken.None);

        Assert.DoesNotContain(planner.LastContext!.Transcript,
            t => t.Text is "OTHER CUSTOMER" or "LAST MONTH" or "NEVER ARRIVED");
    }
}
