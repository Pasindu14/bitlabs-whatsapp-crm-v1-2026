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
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Templates;

/// <summary>
/// The whole point of plan 012: a follow-up that falls outside Meta's 24-hour window goes out as an
/// approved template instead of being dropped.
/// <para>
/// These are the tests that stop the feature being worse than the silence it replaces. An unapproved
/// template, a variable nobody could establish, or 3am in the customer's timezone must each stop the
/// send — a marketing message is charged, attributed to the client's number, and reportable.
/// </para>
/// </summary>
public class FollowUpTemplateTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed record SentTemplate(string Name, string Language, IReadOnlyList<string> Parameters, string Body);

    /// <summary>Records both kinds of send, so a test can assert which path was taken.</summary>
    private sealed class RecordingSender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<string> Texts { get; } = [];
        public List<SentTemplate> Templates { get; } = [];

        public Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            Texts.Add(body);
            return PersistAsync(contact, waba, body, conversationId, ct);
        }

        public Task<Message> SendTemplateAsync(
            Contact contact, WabaConnection waba, string templateName, string language,
            IReadOnlyList<string> parameters, string renderedBody, Guid? conversationId,
            CancellationToken ct = default)
        {
            Templates.Add(new SentTemplate(templateName, language, parameters, renderedBody));
            return PersistAsync(contact, waba, renderedBody, conversationId, ct);
        }

        private async Task<Message> PersistAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId, CancellationToken ct)
        {
            var message = new Message
            {
                Id = Guid.NewGuid(), CompanyId = contact.CompanyId, ContactId = contact.Id,
                WabaConnectionId = waba.Id, ConversationId = conversationId, Body = body,
                Direction = MessageDirection.Outbound, Status = MessageStatus.Sent,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
            };

            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);
            return message;
        }
    }

    /// <summary>Answers the variable question with whatever the test needs, one value per line.</summary>
    private sealed class FakePlanner(string? answer = "hair PRP") : IAiReplyPlanner
    {
        public bool IsConfigured => true;
        public string Model => "claude-haiku-4-5";
        public int Asks { get; private set; }

        public Task<AiReplyPlan> PlanAsync(
            AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default)
            => Task.FromResult(new AiReplyPlan(
                ["still here whenever you're ready"], [], false, false, 10, 5, 0, 0, null));

        public Task<AiAskResult> AskAsync(
            string system, string question, int maxTokens, CancellationToken ct = default)
        {
            Asks++;
            return Task.FromResult(new AiAskResult(answer, new AiUsage(900, 20, 0, 0)));
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

    private sealed class NoopNotifier : IChatNotifier
    {
        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MessageDeletedAsync(
            Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task ConversationAsync(
            Guid companyId, ConversationResponse conversation, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MessageStatusAsync(
            Guid companyId, Guid conversationId, Guid messageId, MessageStatus status, DateTime? statusAt,
            string? errorMessage, CancellationToken ct = default) => Task.CompletedTask;

        public Task BotStateChangedAsync(
            Guid companyId, ConversationResponse conversation, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BotTypingAsync(
            Guid companyId, Guid conversationId, bool isTyping, CancellationToken ct = default)
            => Task.CompletedTask;
    }

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

    private sealed record Fixture(
        AppDbContext Db, ScopedTenantContext Tenant, Conversation Conversation, Contact Contact,
        Template Template);

    /// <summary>
    /// A thread that went quiet YESTERDAY: our message is last, the window has closed, and the step is
    /// due. Open 24/7 unless a test says otherwise, so opening hours only matter where they are the point.
    /// </summary>
    private static async Task<Fixture> SeedAsync(
        TemplateStatus templateStatus = TemplateStatus.Approved,
        bool alwaysOpen = true,
        string templateBody = "Hi {{1}}, just checking in on your {{2}} enquiry. Any questions I can help with?")
    {
        var tenant = new ScopedTenantContext(new HttpContextAccessor());
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"follow-up-template-{Guid.NewGuid()}").Options,
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
            Id = Guid.NewGuid(), CompanyId = CompanyId, Phone = "15550111", Name = "Charith Perera",
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id,
            WabaConnectionId = waba.Id, Status = ConversationStatus.Open,
            LastMessageAt = now.AddDays(-2),
            // Closed yesterday: this is the case that used to be dropped entirely.
            WindowExpiresAt = now.AddDays(-1),
            BotState = ConversationBotState.Active, LastMessageDirection = MessageDirection.Outbound,
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        var ourLastMessage = new Message
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = contact.Id,
            WabaConnectionId = waba.Id, ConversationId = conversation.Id,
            Body = "We've got 10:30 or 2pm on Monday, which suits?",
            Direction = MessageDirection.Outbound, Status = MessageStatus.Sent, IsAutoReply = true,
            CreatedAt = now.AddDays(-2), UpdatedAt = now, IsActive = true,
        };

        var template = new Template
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, WabaConnectionId = waba.Id, WabaId = "waba-1",
            Name = "follow_up_checkin", Language = "en", Category = TemplateCategory.Marketing,
            Status = templateStatus, MetaTemplateId = "meta-1",
            Components = new TemplateComponents
            {
                Body = new TemplateBody { Text = templateBody, Examples = ["Sarah", "hair PRP"] },
            },
            CreatedAt = now, UpdatedAt = now, IsActive = true,
        };

        db.Companies.Add(company);
        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.Messages.Add(ourLastMessage);
        db.Templates.Add(template);
        db.BotProfiles.Add(new BotProfile
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, IsEnabled = true,
            BusinessName = "Marist Polyclinic", BusinessDescription = "A clinic offering hair PRP.",
            DailyReplyLimit = 500,
        });

        if (!alwaysOpen)
        {
            // A one-hour window that is not now, in UTC, so "outside opening hours" is deterministic.
            var awkward = TimeOnly.FromDateTime(now.AddHours(3));
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                db.BusinessHours.Add(new BusinessHours
                {
                    Id = Guid.NewGuid(), CompanyId = CompanyId, DayOfWeek = day,
                    StartLocal = awkward, EndLocal = awkward.AddHours(1),
                    CreatedAt = now, UpdatedAt = now, IsActive = true,
                });
            }
        }

        await db.SaveChangesAsync();
        return new Fixture(db, tenant, conversation, contact, template);
    }

    private static async Task SetStepAsync(Fixture f, Guid? templateId, params string[] overrides)
    {
        var profile = await f.Db.BotProfiles.IgnoreQueryFilters().FirstAsync();
        profile.FollowUpSteps =
        [
            new BotFollowUpStep
            {
                DelayMinutes = 48 * 60,
                Message = "check in on their enquiry",
                TemplateId = templateId,
                VariableOverrides = [.. overrides],
            },
        ];
        await f.Db.SaveChangesAsync();
    }

    private static (BotFollowUpJob Job, RecordingSender Sender) NewJob(
        Fixture f, IAiReplyPlanner? planner = null, RecordingJobClient? jobs = null)
    {
        var sender = new RecordingSender(f.Db);
        var usePlanner = planner ?? new FakePlanner();

        var job = new BotFollowUpJob(
            f.Db, f.Tenant, usePlanner, new StubAvailability(), new StubAppointments(), sender,
            new TemplateVariableResolver(usePlanner, NullLogger<TemplateVariableResolver>.Instance),
            jobs ?? new RecordingJobClient(), new NoopNotifier(), wa_api.Tests.Bot.TestAiUsage.Logger(f.Db),
            NullLogger<BotToolExecutor>.Instance, NullLogger<BotFollowUpJob>.Instance);

        return (job, sender);
    }

    /// <summary>The step is due now, and the silence began two days ago.</summary>
    private static Task RunAsync(BotFollowUpJob job, Fixture f) =>
        job.RunStepAsync(f.Conversation.Id, 0, DateTime.UtcNow.AddDays(-2), CancellationToken.None);

    // ── The window is no longer the end of the road ───────────────────────────

    [Fact]
    public async Task OutsideTheWindow_AnApprovedTemplateIsSent()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        var sent = Assert.Single(sender.Templates);
        Assert.Equal("follow_up_checkin", sent.Name);
        Assert.Equal("en", sent.Language);
        Assert.Empty(sender.Texts);                       // free text is impossible out here
    }

    [Fact]
    public async Task TheFirstVariable_IsTheCustomersFirstName()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        var sent = Assert.Single(sender.Templates);
        Assert.Equal("Charith", sent.Parameters[0]);
    }

    [Fact]
    public async Task TheInboxRow_ShowsTheRenderedMessage_NotThePlaceholders()
    {
        // An agent opening the thread has to see what the customer saw.
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        var sent = Assert.Single(sender.Templates);
        Assert.Equal("Hi Charith, just checking in on your hair PRP enquiry. Any questions I can help with?", sent.Body);
        Assert.DoesNotContain("{{", sent.Body);
    }

    [Fact]
    public async Task AnOverride_BeatsWhateverTheModelWouldSay()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id, "laser treatment");
        var planner = new FakePlanner("hair PRP");
        var (job, sender) = NewJob(f, planner);

        await RunAsync(job, f);

        var sent = Assert.Single(sender.Templates);
        Assert.Equal("laser treatment", sent.Parameters[1]);
        Assert.Equal(0, planner.Asks);                    // nothing left to ask about
    }

    [Fact]
    public async Task InsideTheWindow_TheModelStillWritesTheNudge()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var conversation = await f.Db.Conversations.IgnoreQueryFilters().FirstAsync();
        conversation.WindowExpiresAt = DateTime.UtcNow.AddHours(5);
        await f.Db.SaveChangesAsync();
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Single(sender.Texts);
        Assert.Empty(sender.Templates);                   // a template would cost money for nothing
    }

    // ── Every reason not to send ──────────────────────────────────────────────

    [Fact]
    public async Task AStepWithNoTemplate_IsSkippedOnceTheWindowHasClosed()
    {
        // The owner picks the template on each step; nothing is guessed from the step's delay.
        var f = await SeedAsync();
        await SetStepAsync(f, templateId: null);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
        Assert.Empty(sender.Texts);
    }

    [Theory]
    [InlineData(TemplateStatus.Pending)]
    [InlineData(TemplateStatus.Rejected)]
    [InlineData(TemplateStatus.Paused)]
    [InlineData(TemplateStatus.Disabled)]
    [InlineData(TemplateStatus.Draft)]
    public async Task ATemplateMetaHasNotApproved_IsNeverSent(TemplateStatus status)
    {
        // Checked at send time, not when the owner picked it: Meta can pause a template at any moment.
        var f = await SeedAsync(status);
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
    }

    [Fact]
    public async Task AVariableNobodyCanEstablish_StopsTheSend()
    {
        // Better no follow-up than an approved template carrying a blank or a guess.
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f, new FakePlanner("UNKNOWN"));

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
    }

    [Fact]
    public async Task TheModelBeingUnreachable_StopsTheSend()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f, new FakePlanner(null));

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
    }

    [Fact]
    public async Task AnUpcomingAppointment_StopsTheFollowUpEntirely()
    {
        // The client's own rule: chase only the people who have not booked.
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);

        var type = new AppointmentType
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Name = "Free Consultation",
            DurationMinutes = 30, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
        };
        f.Db.AppointmentTypes.Add(type);
        f.Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ContactId = f.Contact.Id,
            AppointmentTypeId = type.Id, ConversationId = f.Conversation.Id,
            StartUtc = DateTime.UtcNow.AddDays(3), EndUtc = DateTime.UtcNow.AddDays(3).AddMinutes(30),
            Status = AppointmentStatus.Confirmed, Source = AppointmentSource.Bot,
            BlockStartUtc = DateTime.UtcNow.AddDays(3), BlockEndUtc = DateTime.UtcNow.AddDays(3).AddMinutes(30),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task AnOptedOutContact_IsNeverSentATemplate()
    {
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var contact = await f.Db.Contacts.IgnoreQueryFilters().FirstAsync();
        contact.IsOptedOut = true;
        await f.Db.SaveChangesAsync();
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
    }

    // ── Opening hours ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AStepDueOutsideOpeningHours_IsHeldUntilTheyOpen()
    {
        // A marketing message at 3am is what gets a template reported, and a report costs the number
        // its quality rating.
        var f = await SeedAsync(alwaysOpen: false);
        await SetStepAsync(f, f.Template.Id);
        var jobs = new RecordingJobClient();
        var (job, sender) = NewJob(f, jobs: jobs);

        await RunAsync(job, f);

        Assert.Empty(sender.Templates);
        var scheduled = Assert.IsType<ScheduledState>(Assert.Single(jobs.States));
        Assert.InRange((scheduled.EnqueueAt - DateTime.UtcNow).TotalHours, 2.5, 3.5);

        // Nothing was consumed, so the same step still runs when it comes back round.
        var conversation = await f.Db.Conversations.IgnoreQueryFilters().AsNoTracking().FirstAsync();
        Assert.Equal(0, conversation.BotFollowUpStepsSent);
    }

    [Fact]
    public async Task NoOpeningHoursRecorded_MeansSendAnyway()
    {
        // Holding a follow-up forever over a setting nobody filled in would be worse.
        var f = await SeedAsync();
        await SetStepAsync(f, f.Template.Id);
        var (job, sender) = NewJob(f);

        await RunAsync(job, f);

        Assert.Single(sender.Templates);
    }
}
