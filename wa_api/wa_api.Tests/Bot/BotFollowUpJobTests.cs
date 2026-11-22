using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins when the assistant is allowed to speak WITHOUT being spoken to.
/// <para>
/// This is the only message the platform sends unprompted, which makes its guards the most consequential
/// in the feature. A reply that fires wrongly is awkward; an unprompted message that fires wrongly is a
/// business messaging someone who never asked, twice, or after they went to a human — the kind of thing
/// that gets a WhatsApp number reported.
/// </para>
/// </summary>
public class BotFollowUpJobTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class RecordingSender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<string> Sent { get; } = [];

        public async Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            Sent.Add(body);

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

    private sealed class FakePlanner(string? reply = "still here whenever you're ready") : IAiReplyPlanner
    {
        public int Calls { get; private set; }
        public AiTurnContext? LastContext { get; private set; }

        public bool IsConfigured => true;
        public string Model => "claude-haiku-4-5";

        public Task<AiReplyPlan> PlanAsync(
            AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default)
        {
            Calls++;
            LastContext = ctx;

            return Task.FromResult(new AiReplyPlan(
                BubbleSplitter.Split(reply), [], false, false, 100, 20, 0, 0, null));
        }
    }

    private sealed class StubAvailability : IAvailabilityService
    {
        public Task<AvailabilityResponse> GetAsync(
            Guid appointmentTypeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
            => Task.FromResult(new AvailabilityResponse("UTC", default, default, []));

        public Task<bool> IsOfferableAsync(Guid id, DateTime startUtc, CancellationToken ct = default)
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
            CreateAppointmentRequest r, AppointmentSource s, Guid? u, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> RescheduleAsync(
            Guid id, RescheduleAppointmentRequest r, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> CancelAsync(
            Guid id, CancelAppointmentRequest r, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentResponse> UpdateStatusAsync(
            Guid id, UpdateAppointmentStatusRequest r, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed record Fixture(
        AppDbContext Db, ScopedTenantContext Tenant, Conversation Conversation, Contact Contact);

    /// <summary>
    /// A thread the customer has gone quiet on: our message is last, the window is open, the bot owns
    /// it, nothing is booked. Every test then breaks exactly one of those.
    /// </summary>
    private static async Task<Fixture> SeedAsync(bool profileEnabled = true)
    {
        var tenant = new ScopedTenantContext(new HttpContextAccessor());
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"follow-up-{Guid.NewGuid()}").Options,
            tenant);

        var now = DateTime.UtcNow;

        var company = new wa_api.Features.Companies.Company
        {
            Id = CompanyId, Name = $"Clinic {Guid.NewGuid()}", TimeZoneId = "UTC",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var waba = new WabaConnection
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, PhoneNumberId = "pn-1", WabaId = "waba-1",
            DisplayPhoneNumber = "+15550100", EncryptedAccessToken = "token",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var contact = new Contact
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Phone = "15550111", Name = "Mona",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id,
            WabaConnectionId = waba.Id, Status = ConversationStatus.Open,
            LastMessageAt = now, WindowExpiresAt = now.AddHours(20),
            BotState = ConversationBotState.Active, LastMessageDirection = MessageDirection.Outbound,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        // Ours is the last word — that is what "they went quiet" means.
        var ourLastMessage = new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id,
            WabaConnectionId = waba.Id, ConversationId = conversation.Id,
            Body = "We've got 10:30 or 2pm on Monday, which suits?",
            Direction = MessageDirection.Outbound, Status = MessageStatus.Sent, IsAutoReply = true,
            CreatedAt = now.AddMinutes(-BotFollowUpJob.DefaultFollowUpMinutes - 1), UpdatedAt = now, IsActive = true,
        };

        db.Companies.Add(company);
        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.Messages.Add(ourLastMessage);
        db.BotProfiles.Add(new BotProfile
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, IsEnabled = profileEnabled,
            BusinessName = "the clinic", BusinessDescription = "A doctor's clinic.",
            DailyReplyLimit = 500,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        });

        await db.SaveChangesAsync();
        return new Fixture(db, tenant, conversation, contact);
    }

    /// <summary>Records what the job scheduled, so the next step can be asserted without Hangfire.</summary>
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

    private static (BotFollowUpJob Job, RecordingSender Sender) NewJob(
        Fixture f, IAiReplyPlanner planner, RecordingJobClient? jobClient = null)
    {
        var (job, sender, _) = NewJobWithNotifier(f, planner, jobClient);
        return (job, sender);
    }

    private static (BotFollowUpJob Job, RecordingSender Sender, RecordingNotifier Notifier) NewJobWithNotifier(
        Fixture f, IAiReplyPlanner planner, RecordingJobClient? jobClient = null)
    {
        var sender = new RecordingSender(f.Db);
        var notifier = new RecordingNotifier();
        var job = new BotFollowUpJob(
            f.Db, f.Tenant, planner, new StubAvailability(), new StubAppointments(), sender,
            new TemplateVariableResolver(planner, NullLogger<TemplateVariableResolver>.Instance),
            jobClient ?? new RecordingJobClient(), notifier, TestAiUsage.Logger(f.Db),
            NullLogger<BotToolExecutor>.Instance, NullLogger<BotFollowUpJob>.Instance);

        return (job, sender, notifier);
    }

    private sealed class RecordingNotifier : IChatNotifier
    {
        public List<ConversationMessageResponse> Messages { get; } = [];

        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

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

    private static async Task SetStepsAsync(Fixture f, params (int DelayMinutes, string Message)[] steps)
    {
        var profile = await f.Db.BotProfiles.IgnoreQueryFilters().FirstAsync();
        profile.FollowUpSteps = [.. steps.Select(s => new BotFollowUpStep
        {
            DelayMinutes = s.DelayMinutes, Message = s.Message,
        })];

        // The run fires once the first step's delay has passed, so our last word is at least that old.
        var ours = await f.Db.Messages.IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound).ToListAsync();
        foreach (var m in ours) m.CreatedAt = DateTime.UtcNow.AddMinutes(-steps.Min(x => x.DelayMinutes) - 1);

        await f.Db.SaveChangesAsync();
    }

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task AQuietThread_GetsExactlyOneNudge()
    {
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.NotNull((await f.Db.Conversations.IgnoreQueryFilters().FirstAsync()).BotFollowUpSentAt);
    }

    [Fact]
    public async Task TheNudge_IsPushedToTheOpenInbox()
    {
        // Without the push, agents only saw the nudge after switching chats.
        var f = await SeedAsync();
        var (job, sender, notifier) = NewJobWithNotifier(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        var pushed = Assert.Single(notifier.Messages);
        Assert.Equal(sender.Sent[0], pushed.Body);
    }

    [Fact]
    public async Task AnEarlierRepliesRun_DoesNotNudgeAConversationThatCarriedOn()
    {
        // Every reply schedules its own run. The first reply's run fires on time, but the chat carried on
        // and our latest message is two minutes old — nudging now would chase after two minutes of quiet.
        var f = await SeedAsync();
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId, ConversationId = f.Conversation.Id,
            Body = "Booked you a spot? Just say the word.", Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent, IsAutoReply = true, CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Equal(0, planner.Calls);
        // Not stamped either — the latest reply's own run must still be free to nudge.
        Assert.Null((await f.Db.Conversations.IgnoreQueryFilters().FirstAsync()).BotFollowUpSentAt);
    }

    [Fact]
    public async Task TheModelIsToldThisIsAFollowUp()
    {
        // Nothing new has been said, so without this the model answers a question nobody asked or
        // simply repeats its last message.
        var f = await SeedAsync();
        var planner = new FakePlanner();
        var (job, _) = NewJob(f, planner);

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.True(planner.LastContext!.IsFollowUp);
    }

    [Fact]
    public async Task OnlyTheFirstBubbleIsSent()
    {
        // Two unprompted messages after silence reads as chasing, which is the opposite of the point.
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner("still here\n\nwant me to hold 10:30?"));

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("still here", sender.Sent[0]);
    }

    // ── The guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheyReplied_SoThereIsNothingToChase()
    {
        var f = await SeedAsync();
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId, ConversationId = f.Conversation.Id,
            Body = "10:30 works", Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered, CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AlreadyFollowedUp_NeverTwiceForOneSilence()
    {
        var f = await SeedAsync();
        f.Conversation.BotFollowUpSentAt = DateTime.UtcNow.AddMinutes(-5);
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ClosedWindow_StaysSilent()
    {
        // The hard external limit. Outside the window Meta requires an approved template, and this
        // platform has no template send at all — so the only correct behaviour is not to try.
        var f = await SeedAsync();
        f.Conversation.WindowExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AHumanTookOver_SoTheBotDoesNotChase()
    {
        var f = await SeedAsync();
        f.Conversation.BotState = ConversationBotState.Paused;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OptedOutContact_IsNeverChased()
    {
        var f = await SeedAsync();
        f.Contact.IsOptedOut = true;
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AlreadyBooked_NothingToNudgeThemTowards()
    {
        var f = await SeedAsync();
        await AddAppointmentAsync(f, DateTime.UtcNow.AddDays(1), AppointmentStatus.Confirmed);

        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task BookedOnceBefore_ButNothingComingUp_StillGetsANudge()
    {
        // BotConvertedAt is never cleared. A returning customer whose last visit has gone by has nothing
        // booked, and going quiet mid-conversation is exactly when the nudge is for.
        var f = await SeedAsync();
        f.Conversation.BotConvertedAt = DateTime.UtcNow.AddDays(-7);
        await f.Db.SaveChangesAsync();
        await AddAppointmentAsync(f, DateTime.UtcNow.AddDays(-7), AppointmentStatus.Completed);

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        var booking = Assert.Single(planner.LastContext!.Bookings!);
        Assert.False(booking.IsUpcoming);
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

    [Fact]
    public async Task AskedEnoughAlready_SilenceIsAnAnswer()
    {
        // Someone asked three times who then goes quiet has answered by not answering.
        var f = await SeedAsync();
        f.Conversation.BotAsksMade = 3;
        await f.Db.SaveChangesAsync();

        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DisabledAssistant_SendsNothing()
    {
        var f = await SeedAsync(profileEnabled: false);
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task NothingWorthSaying_SendsNothing()
    {
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner(reply: "   "));

        await job.RunAsync(f.Conversation.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AMissingConversation_IsANoOpRatherThanAThrow()
    {
        // Unlike the reply job, there is no enqueue/commit race here — the conversation existed when
        // this was scheduled. A missing one means it was deleted, and retrying cannot help.
        var f = await SeedAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    // ── The sequence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AStep_CarriesTheOwnersScriptToTheModel()
    {
        var f = await SeedAsync();
        await SetStepsAsync(f, (360, "I can hold a free consultation slot. Weekday or weekend?"));
        var planner = new FakePlanner();
        var (job, sender) = NewJob(f, planner);

        await job.RunStepAsync(f.Conversation.Id, 0, DateTime.UtcNow.AddHours(-6), CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("I can hold a free consultation slot. Weekday or weekend?", planner.LastContext!.FollowUpScript);
    }

    [Fact]
    public async Task ASentStep_SchedulesTheNextOne_FromTheSameAnchor()
    {
        // Delays count from when the silence began, so "12 hours" means twelve hours after our reply,
        // not twelve hours after the previous nudge.
        var f = await SeedAsync();
        await SetStepsAsync(f, (60, "first"), (720, "second"));
        var jobs = new RecordingJobClient();
        var (job, _) = NewJob(f, new FakePlanner(), jobs);
        var anchor = DateTime.UtcNow.AddMinutes(-60);

        await job.RunStepAsync(f.Conversation.Id, 0, anchor, CancellationToken.None);

        var scheduled = Assert.IsType<ScheduledState>(Assert.Single(jobs.States));
        Assert.InRange((scheduled.EnqueueAt - anchor).TotalMinutes, 719, 721);
        Assert.Equal(1, Assert.Single(jobs.Created).Args[1]);
        Assert.Equal(1, (await f.Db.Conversations.IgnoreQueryFilters().FirstAsync()).BotFollowUpStepsSent);
    }

    [Fact]
    public async Task ANextStepPastTheWindow_IsStillScheduled()
    {
        // Day 2 falls outside Meta's 24-hour window, so it goes out as a template. It is scheduled
        // regardless: whether a usable template exists is checked when it arrives.
        var f = await SeedAsync();
        await SetStepsAsync(f, (360, "first"), (2 * 24 * 60, "day two"));
        var jobs = new RecordingJobClient();
        var (job, sender) = NewJob(f, new FakePlanner(), jobs);

        await job.RunStepAsync(f.Conversation.Id, 0, DateTime.UtcNow.AddHours(-6), CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Single(jobs.States);
    }

    [Fact]
    public async Task TheLastStep_SchedulesNothingFurther()
    {
        var f = await SeedAsync();
        await SetStepsAsync(f, (60, "only"));
        var jobs = new RecordingJobClient();
        var (job, sender) = NewJob(f, new FakePlanner(), jobs);

        await job.RunStepAsync(f.Conversation.Id, 0, DateTime.UtcNow.AddHours(-1), CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Empty(jobs.States);
    }

    [Fact]
    public async Task AStepAlreadySent_NeverFiresTwice()
    {
        var f = await SeedAsync();
        await SetStepsAsync(f, (60, "first"), (120, "second"));
        f.Conversation.BotFollowUpStepsSent = 2;
        await f.Db.SaveChangesAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunStepAsync(f.Conversation.Id, 1, DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AJobFromAnEarlierSilence_StandsDown()
    {
        // They wrote after this job's anchor, so a newer reply started its own sequence — this one is
        // stale even though our message happens to be the latest again.
        var f = await SeedAsync();
        var anchor = DateTime.UtcNow.AddHours(-3);
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId, ConversationId = f.Conversation.Id,
            Body = "what about parking?", Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered, CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        f.Db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id,
            WabaConnectionId = f.Conversation.WabaConnectionId, ConversationId = f.Conversation.Id,
            Body = "There's paid parking in the building.", Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent, CreatedAt = DateTime.UtcNow.AddMinutes(-29),
            UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunStepAsync(f.Conversation.Id, 0, anchor, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AShortenedSequence_StopsTheStepsThatNoLongerExist()
    {
        var f = await SeedAsync();
        await SetStepsAsync(f, (60, "only"));
        f.Conversation.BotFollowUpStepsSent = 1;
        await f.Db.SaveChangesAsync();
        var (job, sender) = NewJob(f, new FakePlanner());

        await job.RunStepAsync(f.Conversation.Id, 1, DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void NoConfiguredSequence_IsTheSingleNudge()
    {
        var steps = BotFollowUpJob.StepsFor(new BotProfile());

        var only = Assert.Single(steps);
        Assert.Equal(BotFollowUpJob.DefaultFollowUpMinutes, only.DelayMinutes);
        Assert.True(string.IsNullOrEmpty(only.Message));
    }

    [Fact]
    public void StepSavedBeforeTemplates_HasNoOverridesRatherThanThrowing()
    {
        // Steps stored before plan 012 have no VariableOverrides key in their JSON, so it loads as null.
        var profile = new BotProfile
        {
            FollowUpSteps = [new BotFollowUpStep { DelayMinutes = 360, Message = "Hi", VariableOverrides = null! }],
        };

        var only = Assert.Single(BotFollowUpJob.StepsFor(profile));
        Assert.Equal(360, only.DelayMinutes);
        Assert.Empty(only.VariableOverrides);
    }
}
