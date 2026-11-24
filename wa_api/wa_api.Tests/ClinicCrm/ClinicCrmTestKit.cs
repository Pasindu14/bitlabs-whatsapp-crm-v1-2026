using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.ClinicCrm.Entities;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// A clinic in Dubai, connected to its CRM, with one customer mid-conversation and one bookable service
/// mapped to CRM treatment 1.
/// </summary>
internal sealed class ClinicCrmTestKit
{
    public static readonly TimeZoneInfo Dubai = CompanyTimeZone.ResolveOrFallback("Asia/Dubai");
    public const int TreatmentId = 1;

    public required AppDbContext Db { get; init; }
    public required ScopedTenantContext Tenant { get; init; }
    public required Guid CompanyId { get; init; }
    public required Contact Contact { get; init; }
    public required Conversation Conversation { get; init; }
    public required AppointmentType Service { get; init; }
    public required ClinicCrmConnection Connection { get; init; }

    public ClinicCrmEndpoint Endpoint =>
        new(Connection.Id, Connection.BaseUrl, Connection.ClientId, Connection.ClientSecret);

    public static async Task<ClinicCrmTestKit> CreateAsync(bool mapped = true)
    {
        var companyId = Guid.NewGuid();
        var tenant = new ScopedTenantContext(new HttpContextAccessor());
        tenant.EnterCompany(companyId);

        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"clinic-crm-{Guid.NewGuid()}")
                .AddInterceptors(new AuditInterceptor(new HttpContextAccessor()))
                .Options,
            tenant);

        var now = DateTime.UtcNow;

        var waba = new WabaConnection
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PhoneNumberId = $"pn-{Guid.NewGuid():N}", WabaId = "waba-1",
            DisplayPhoneNumber = "+971500000001", EncryptedAccessToken = "token", IsActive = true,
        };

        var contact = new Contact
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Phone = "971501234567", Name = "Sarah Fernando", IsActive = true,
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = companyId, ContactId = contact.Id, WabaConnectionId = waba.Id,
            Status = ConversationStatus.Open, LastMessageAt = now, WindowExpiresAt = now.AddHours(20),
            BotState = ConversationBotState.Active, LastMessageDirection = MessageDirection.Inbound, IsActive = true,
        };

        var service = new AppointmentType
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Name = "PRP Consultation", DurationMinutes = 30,
            SlotIntervalMinutes = 15, IsEnabled = true, IsActive = true,
            ExternalTreatmentId = mapped ? TreatmentId : null,
        };

        var connection = new ClinicCrmConnection
        {
            Id = Guid.NewGuid(), CompanyId = companyId, BaseUrl = "http://crm.test/api/bot",
            ClientId = "gdc_test", ClientSecret = "gds_test", IsEnabled = true, IsActive = true,
        };

        db.Companies.Add(new wa_api.Features.Companies.Company
        {
            Id = companyId, Name = $"Clinic {Guid.NewGuid()}", TimeZoneId = "Asia/Dubai", IsActive = true,
        });
        db.WabaConnections.Add(waba);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.AppointmentTypes.Add(service);
        db.ClinicCrmConnections.Add(connection);
        await db.SaveChangesAsync();

        return new ClinicCrmTestKit
        {
            Db = db, Tenant = tenant, CompanyId = companyId, Contact = contact,
            Conversation = conversation, Service = service, Connection = connection,
        };
    }

    public AppointmentService Appointments(IClinicCrmBookingService? crm = null) =>
        // The mirror and CRM paths never consult our own availability — the CRM decided.
        new(Db, null!, NullLogger<AppointmentService>.Instance, crm);

    /// <summary>A day comfortably in the future, as the clinic sees it.</summary>
    public static DateOnly DaysAhead(int days) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Dubai)).AddDays(days);

    public static DateTime DubaiToUtc(DateOnly date, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Dubai);

    public static ClinicCrmBookingChange Booking(int id, DateOnly date, string start, string end, string action = "booked") =>
        new(action, null, new ClinicCrmBooking(id, 1052, "Sarah Fernando", date.ToString("yyyy-MM-dd"), start, end,
            [new ClinicCrmBookingTreatment(TreatmentId, "Consultation")], "Booked", null));

    public static Dictionary<string, JsonElement> Args(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));
}

/// <summary>A scriptable stand-in for the CRM, one level above HTTP.</summary>
internal sealed class FakeClinicCrmBookings : IClinicCrmBookingService
{
    public ClinicCrmEndpoint? Endpoint { get; set; }
    public TimeZoneInfo CrmTimeZone => ClinicCrmTestKit.Dubai;

    public Func<DateOnly, ClinicCrmResult<ClinicCrmAvailability>> OnAvailability { get; set; } =
        d => new(ClinicCrmOutcome.Ok, new ClinicCrmAvailability(d.ToString("yyyy-MM-dd"), true, "09:00", "18:00", 45, []));

    public Func<ClinicCrmBookingDraft, ClinicCrmResult<ClinicCrmBookingChange>> OnBook { get; set; } =
        _ => throw new InvalidOperationException("Booking was not expected.");

    public Func<int, DateTime, ClinicCrmResult<ClinicCrmBookingChange>> OnMove { get; set; } =
        (_, _) => throw new InvalidOperationException("A move was not expected.");

    public Func<int, ClinicCrmCancelResult> OnCancel { get; set; } =
        _ => throw new InvalidOperationException("A cancellation was not expected.");

    public List<(int BookingId, string Phone, string? Reason)> CancellationRequests { get; } = [];

    /// <summary>Bookings the clinic holds that never went through the chat — made by phone.</summary>
    public List<ClinicCrmBooking> PhoneBookings { get; } = [];

    /// <summary>Overrides the clinic's list entirely (e.g. to make it unreachable).</summary>
    public Func<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>>? OnUpcoming { get; set; }

    private readonly ClinicCrmTestKit _kit;

    public List<DateOnly> AvailabilityAsked { get; } = [];

    /// <summary>What the chat summary comes back as (null = none).</summary>
    public string? Summary { get; set; }
    public List<ClinicCrmBookingDraft> Books { get; } = [];
    public List<(int BookingId, string Phone, DateTime StartUtc)> Moves { get; } = [];
    public List<Guid> Queued { get; } = [];
    public List<(string Title, string Body)> Notifications { get; } = [];

    public FakeClinicCrmBookings(ClinicCrmTestKit kit)
    {
        _kit = kit;
        Endpoint = kit.Endpoint;
    }

    /// <summary>
    /// What the clinic would list: every live booking we synced to it, plus <see cref="PhoneBookings"/>.
    /// </summary>
    public async Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetUpcomingAsync(
        ClinicCrmEndpoint endpoint, string phone, CancellationToken ct = default)
    {
        if (OnUpcoming is not null) return OnUpcoming();

        var synced = await _kit.Db.Appointments.AsNoTracking()
            .Where(a => a.ExternalSyncStatus == ExternalSyncStatus.Synced && a.ExternalBookingId != null
                     && a.Status != AppointmentStatus.Cancelled)
            .ToListAsync(ct);

        var list = synced.Select(a =>
        {
            var start = TimeZoneInfo.ConvertTimeFromUtc(a.StartUtc, ClinicCrmTestKit.Dubai);
            var end = TimeZoneInfo.ConvertTimeFromUtc(a.EndUtc, ClinicCrmTestKit.Dubai);
            return new ClinicCrmBooking(a.ExternalBookingId!.Value, 1052, "Sarah Fernando",
                start.ToString("yyyy-MM-dd"), start.ToString("HH:mm"), end.ToString("HH:mm"),
                [new ClinicCrmBookingTreatment(ClinicCrmTestKit.TreatmentId, "Hair Treatment")], "Booked", null);
        }).Concat(PhoneBookings).ToList();

        return new(ClinicCrmOutcome.Ok, list);
    }

    public Task<ClinicCrmEndpoint?> GetEndpointAsync(CancellationToken ct = default) => Task.FromResult(Endpoint);

    /// <summary>The endpoint the message notification sees; null = the switch is off.</summary>
    public ClinicCrmEndpoint? NotifyEndpoint { get; set; }

    /// <summary>Replies to POST /messages, in order; the last one repeats.</summary>
    public Queue<ClinicCrmOutcome> NotifyReplies { get; } = new();
    public List<string> MessageNotifications { get; } = [];

    public Task<ClinicCrmEndpoint?> GetMessageNotifyEndpointAsync(CancellationToken ct = default) =>
        Task.FromResult(NotifyEndpoint);

    public Task<ClinicCrmResult<JsonElement>> NotifyMessageAsync(
        ClinicCrmEndpoint endpoint, string phone, CancellationToken ct = default)
    {
        MessageNotifications.Add(phone);
        var outcome = NotifyReplies.Count > 1 ? NotifyReplies.Dequeue()
            : NotifyReplies.Count == 1 ? NotifyReplies.Peek() : ClinicCrmOutcome.Ok;
        return Task.FromResult(new ClinicCrmResult<JsonElement>(outcome));
    }

    public Task<string?> SummarizeConversationAsync(
        Guid companyId, Guid conversationId, string serviceName, CancellationToken ct = default) =>
        Task.FromResult(Summary);

    public List<(string Name, string Phone)> SavedCustomers { get; } = [];

    public Task<ClinicCrmResult<ClinicCrmCustomer>> SaveInterestedCustomerAsync(
        ClinicCrmEndpoint endpoint, string name, string phone, CancellationToken ct = default)
    {
        SavedCustomers.Add((name, phone));
        return Task.FromResult(new ClinicCrmResult<ClinicCrmCustomer>(
            ClinicCrmOutcome.Ok, new ClinicCrmCustomer(1052, "created", name, null)));
    }

    public Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
        ClinicCrmEndpoint endpoint, DateOnly date, CancellationToken ct = default)
    {
        AvailabilityAsked.Add(date);
        return Task.FromResult(OnAvailability(date));
    }

    public Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmBookingDraft draft, CancellationToken ct = default)
    {
        Books.Add(draft);
        return Task.FromResult(OnBook(draft));
    }

    public Task<ClinicCrmResult<ClinicCrmBookingChange>> MoveAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, DateTime newStartUtc, CancellationToken ct = default)
    {
        Moves.Add((bookingId, phone, newStartUtc));
        return Task.FromResult(OnMove(bookingId, newStartUtc));
    }

    public Task<ClinicCrmCancelResult> CancelBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default)
    {
        CancellationRequests.Add((bookingId, phone, reason));
        return Task.FromResult(OnCancel(bookingId));
    }

    public Task<ClinicCrmResult<ClinicCrmBookingChange>> RequestCancellationAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default) =>
        throw new InvalidOperationException("Callers go through CancelBookingAsync.");

    public void QueueSync(Guid appointmentId) => Queued.Add(appointmentId);

    public Task NotifyStaffAsync(Guid companyId, string title, string body, CancellationToken ct = default)
    {
        Notifications.Add((title, body));
        return Task.CompletedTask;
    }
}

/// <summary>Records what was scheduled, without Hangfire.</summary>
internal sealed class RecordingJobs : IBackgroundJobClient
{
    public List<(Job Job, IState State)> Created { get; } = [];

    public string Create(Job job, IState state)
    {
        Created.Add((job, state));
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
