using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>Save-then-book, the 404 retry, and the connection's recorded health.</summary>
public class ClinicCrmBookingServiceTests
{
    private sealed class ScriptedClient : IClinicCrmClient
    {
        public Queue<ClinicCrmResult<ClinicCrmCustomer>> CustomerReplies { get; } = new();
        public Queue<ClinicCrmResult<ClinicCrmBookingChange>> BookReplies { get; } = new();
        public List<ClinicCrmSaveCustomerRequest> SavedCustomers { get; } = [];
        public List<ClinicCrmBookRequest> BookRequests { get; } = [];
        public IReadOnlyList<ClinicCrmBooking> Upcoming { get; set; } = [];
        public ClinicCrmResult<JsonElement> CancelReply { get; set; } = new(ClinicCrmOutcome.Ok);
        public List<(int BookingId, ClinicCrmCancelBookingRequest Request)> Cancels { get; } = [];

        public Task<ClinicCrmResult<JsonElement>> CancelBookingAsync(
            ClinicCrmEndpoint e, int bookingId, ClinicCrmCancelBookingRequest request, CancellationToken ct = default)
        {
            Cancels.Add((bookingId, request));
            return Task.FromResult(CancelReply);
        }
        public List<(int BookingId, ClinicCrmUpdateBookingRequest Request)> Updates { get; } = [];

        public Task<ClinicCrmResult<ClinicCrmToken>> GetTokenAsync(ClinicCrmEndpoint e, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ClinicCrmResult<IReadOnlyList<ClinicCrmTreatment>>> GetTreatmentsAsync(
            ClinicCrmEndpoint e, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ClinicCrmResult<ClinicCrmCustomer>> SaveCustomerAsync(
            ClinicCrmEndpoint e, ClinicCrmSaveCustomerRequest request, CancellationToken ct = default)
        {
            SavedCustomers.Add(request);
            return Task.FromResult(CustomerReplies.Count > 1 ? CustomerReplies.Dequeue() : CustomerReplies.Peek());
        }

        public Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
            ClinicCrmEndpoint e, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(new ClinicCrmResult<ClinicCrmAvailability>(ClinicCrmOutcome.Unavailable, Message: "down"));

        public Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
            ClinicCrmEndpoint e, ClinicCrmBookRequest request, CancellationToken ct = default)
        {
            BookRequests.Add(request);
            return Task.FromResult(BookReplies.Count > 1 ? BookReplies.Dequeue() : BookReplies.Peek());
        }

        public Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetBookingsAsync(
            ClinicCrmEndpoint e, string whatsapp, CancellationToken ct = default) =>
            Task.FromResult(new ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>(ClinicCrmOutcome.Ok, Upcoming));

        public Task<ClinicCrmResult<ClinicCrmBookingChange>> UpdateBookingAsync(
            ClinicCrmEndpoint e, int bookingId, ClinicCrmUpdateBookingRequest request, CancellationToken ct = default)
        {
            Updates.Add((bookingId, request));
            var booking = Upcoming.First(b => b.BookingId == bookingId) with { Notes = request.Notes };
            return Task.FromResult(new ClinicCrmResult<ClinicCrmBookingChange>(
                ClinicCrmOutcome.Ok, new ClinicCrmBookingChange("updated", null, booking)));
        }
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task CreateAsync(Guid companyId, NotificationType type, string title, string body, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
            string title, string body, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkReadAsync(Guid notificationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ClinicCrmBookingService Service(ClinicCrmTestKit kit, IClinicCrmClient client) =>
        new(kit.Db, client, Options.Create(new ClinicCrmOptions()), new RecordingJobs(), new NoNotifications(),
            NullLogger<ClinicCrmBookingService>.Instance);

    private static readonly ClinicCrmResult<ClinicCrmCustomer> Saved =
        new(ClinicCrmOutcome.Ok, new ClinicCrmCustomer(1052, "created", "Sarah Fernando", "Interested"));

    private static ClinicCrmBookingDraft Draft() => new(
        "Sarah Fernando", "971501234567", ClinicCrmTestKit.TreatmentId,
        new DateTime(2030, 10, 5, 12, 0, 0, DateTimeKind.Utc), "Asked about pricing");

    [Fact]
    public async Task SavesTheCustomer_ThenBooks_InDubaiTime()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(Saved);
        client.BookReplies.Enqueue(new(ClinicCrmOutcome.Ok,
            ClinicCrmTestKit.Booking(873, new DateOnly(2030, 10, 5), "16:00", "16:45")));

        var result = await Service(kit, client).BookAsync(kit.Endpoint, Draft());

        Assert.True(result.IsOk);

        var customer = Assert.Single(client.SavedCustomers);
        Assert.Equal("+971501234567", customer.Whatsapp);
        Assert.Equal([ClinicCrmTestKit.TreatmentId], customer.TreatmentIds);

        var booking = Assert.Single(client.BookRequests);
        Assert.Equal("2030-10-05", booking.Date);
        Assert.Equal("16:00", booking.StartTime);       // 12:00 UTC is 16:00 in Dubai
        Assert.Equal("Asked about pricing", booking.Notes);
    }

    [Fact]
    public async Task TheCustomer_IsSavedWithoutANote_EvenWhenTheBookingIsRetried()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(Saved);
        client.BookReplies.Enqueue(new(ClinicCrmOutcome.NotFound, ErrorField: "whatsapp"));
        client.BookReplies.Enqueue(new(ClinicCrmOutcome.Ok,
            ClinicCrmTestKit.Booking(873, new DateOnly(2030, 10, 5), "16:00", "16:45")));

        await Service(kit, client).BookAsync(kit.Endpoint, Draft() with { Notes = "Concern: hair fall" });

        Assert.Equal([null, null], client.SavedCustomers.Select(c => c.Notes));
    }

    [Fact]
    public async Task AnInterestedCustomer_IsSavedWithNoTreatment_AndNoNote()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(Saved);

        var result = await Service(kit, client).SaveInterestedCustomerAsync(kit.Endpoint, " Mona ", "971501234567");

        Assert.True(result.IsOk);
        var sent = Assert.Single(client.SavedCustomers);
        Assert.Equal("Mona", sent.Name);
        Assert.Equal("+971501234567", sent.Whatsapp);
        Assert.Empty(sent.TreatmentIds);
        Assert.Null(sent.Notes);
    }

    [Fact]
    public async Task ABookingWithNoTreatment_SendsAnEmptyTreatmentList()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(Saved);

        client.BookReplies.Enqueue(new(ClinicCrmOutcome.Rejected, Message: "stop here"));

        await Service(kit, client).BookAsync(kit.Endpoint, Draft() with { TreatmentId = null });

        Assert.Empty(Assert.Single(client.SavedCustomers).TreatmentIds);
        Assert.Empty(Assert.Single(client.BookRequests).TreatmentIds);
    }

    [Fact]
    public async Task CustomerUnknownOnBooking_SavesAgain_AndRetriesOnce()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(Saved);
        client.BookReplies.Enqueue(new(ClinicCrmOutcome.NotFound, ErrorField: "whatsapp"));
        client.BookReplies.Enqueue(new(ClinicCrmOutcome.Ok,
            ClinicCrmTestKit.Booking(873, new DateOnly(2030, 10, 5), "16:00", "16:45")));

        var result = await Service(kit, client).BookAsync(kit.Endpoint, Draft());

        Assert.True(result.IsOk);
        Assert.Equal(2, client.SavedCustomers.Count);
        Assert.Equal(2, client.BookRequests.Count);
    }

    [Fact]
    public async Task ACustomerTheClinicWillNotSave_IsNeverBooked()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient();
        client.CustomerReplies.Enqueue(new(ClinicCrmOutcome.Unavailable, Message: "down"));

        var result = await Service(kit, client).BookAsync(kit.Endpoint, Draft());

        Assert.Equal(ClinicCrmOutcome.Unavailable, result.Outcome);
        Assert.Empty(client.BookRequests);
    }

    [Fact]
    public async Task AnUnreachableClinic_IsRecordedOnTheConnection()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();

        await Service(kit, new ScriptedClient()).GetAvailabilityAsync(kit.Endpoint, new DateOnly(2030, 10, 5));

        var connection = await kit.Db.ClinicCrmConnections.AsNoTracking().SingleAsync();
        Assert.Equal("down", connection.LastError);
        Assert.NotNull(connection.LastErrorAt);
    }

    private static ClinicCrmBooking Upcoming(int id, string? notes) =>
        ClinicCrmTestKit.Booking(id, new DateOnly(2030, 10, 5), "16:00", "16:45").Booking with { Notes = notes };

    [Fact]
    public async Task RequestingACancellation_FlagsTheBooking_AndKeepsTheClinicsOwnNote()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient { Upcoming = [Upcoming(873, "Prefers Dr. Martin")] };

        var result = await Service(kit, client).RequestCancellationAsync(kit.Endpoint, 873, "971501234567", "Travelling");

        Assert.True(result.IsOk);
        var (id, request) = Assert.Single(client.Updates);
        Assert.Equal(873, id);
        Assert.Equal("+971501234567", request.Whatsapp);
        Assert.Null(request.Date);                      // a note change only — never a move
        Assert.StartsWith(ClinicCrmBookingService.CancellationFlag, request.Notes);
        Assert.Contains("reason: Travelling", request.Notes);
        Assert.EndsWith("\n\nPrefers Dr. Martin", request.Notes);
    }

    [Fact]
    public async Task Cancelling_UsesTheClinicsCancel_AndNeedsNoNote()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient { Upcoming = [Upcoming(873, null)] };

        var result = await Service(kit, client).CancelBookingAsync(kit.Endpoint, 873, "971501234567", "Travelling");

        Assert.Equal(ClinicCrmCancelOutcome.Cancelled, result.Outcome);
        var (id, request) = Assert.Single(client.Cancels);
        Assert.Equal(873, id);
        Assert.Equal(new ClinicCrmCancelBookingRequest("+971501234567", "Travelling"), request);
        Assert.Empty(client.Updates);
    }

    [Fact]
    public async Task ABookingTheClinicWillNotCancel_IsFlaggedInstead()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient
        {
            Upcoming = [Upcoming(873, null)],
            CancelReply = new(ClinicCrmOutcome.Taken, Message: "Too close to the appointment."),
        };

        var result = await Service(kit, client).CancelBookingAsync(kit.Endpoint, 873, "971501234567", null);

        Assert.Equal(ClinicCrmCancelOutcome.Flagged, result.Outcome);
        Assert.StartsWith(ClinicCrmBookingService.CancellationFlag, Assert.Single(client.Updates).Request.Notes);
    }

    [Fact]
    public async Task AnUnreachableClinic_CancelsNothing_AndFlagsNothing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient
        {
            Upcoming = [Upcoming(873, null)],
            CancelReply = new(ClinicCrmOutcome.Unavailable, Message: "down"),
        };

        var result = await Service(kit, client).CancelBookingAsync(kit.Endpoint, 873, "971501234567", null);

        Assert.Equal(ClinicCrmCancelOutcome.Unavailable, result.Outcome);
        Assert.Empty(client.Updates);
    }

    [Fact]
    public async Task ABookingAlreadyFlagged_IsNotFlaggedTwice()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient { Upcoming = [Upcoming(873, "CANCELLATION REQUESTED 2030-10-01 10:00 …")] };

        var result = await Service(kit, client).RequestCancellationAsync(kit.Endpoint, 873, "971501234567", null);

        Assert.True(result.IsOk);
        Assert.Empty(client.Updates);
    }

    [Fact]
    public async Task ABookingNoLongerUpcomingAtTheClinic_IsNotFound()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var client = new ScriptedClient { Upcoming = [Upcoming(900, null)] };

        var result = await Service(kit, client).RequestCancellationAsync(kit.Endpoint, 873, "971501234567", null);

        Assert.Equal(ClinicCrmOutcome.NotFound, result.Outcome);
        Assert.Empty(client.Updates);
    }

    [Fact]
    public async Task OnlyAnEnabledLiveConnection_CountsAsConnected()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var service = Service(kit, new ScriptedClient());

        Assert.NotNull(await service.GetEndpointAsync());

        kit.Connection.IsEnabled = false;
        await kit.Db.SaveChangesAsync();

        Assert.Null(await service.GetEndpointAsync());
    }
}
