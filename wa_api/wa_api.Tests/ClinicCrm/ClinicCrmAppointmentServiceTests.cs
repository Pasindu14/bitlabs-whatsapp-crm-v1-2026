using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// Staff moving or cancelling a CRM booking from our calendar: the CRM goes first, and a refusal changes
/// nothing on either side.
/// </summary>
public class ClinicCrmAppointmentServiceTests
{
    private static async Task<Appointment> SeedAsync(
        ClinicCrmTestKit kit, DateOnly day, ExternalSyncStatus sync = ExternalSyncStatus.Synced, int? bookingId = 873)
    {
        var start = ClinicCrmTestKit.DubaiToUtc(day, 16);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), CompanyId = kit.CompanyId, ContactId = kit.Contact.Id,
            AppointmentTypeId = kit.Service.Id, ConversationId = kit.Conversation.Id,
            StartUtc = start, EndUtc = start.AddMinutes(45), BlockStartUtc = start, BlockEndUtc = start.AddMinutes(45),
            Status = AppointmentStatus.Confirmed, Source = AppointmentSource.Bot, IsActive = true,
            ExternalSyncStatus = sync, ExternalBookingId = bookingId,
        };
        kit.Db.Appointments.Add(appointment);
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();
        return appointment;
    }

    private static Task<Appointment> StoredAsync(ClinicCrmTestKit kit, Guid id) =>
        kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);

    [Fact]
    public async Task Rescheduling_MovesItAtTheClinicFirst_AndKeepsTheNewBookingId()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnMove = (_, _) => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(874, day.AddDays(1), "10:30", "11:15", "rescheduled")),
        };

        var result = await kit.Appointments(crm).RescheduleAsync(
            booking.Id, new RescheduleAppointmentRequest(ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 10, 30)));

        Assert.Equal(874, result.ExternalBookingId);
        Assert.Equal(ExternalSyncStatus.Synced, result.ExternalSyncStatus);
        Assert.Equal((873, "971501234567", ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 10, 30)), Assert.Single(crm.Moves));

        var stored = await StoredAsync(kit, booking.Id);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 11, 15), stored.EndUtc);
    }

    [Fact]
    public async Task ReschedulingToATimeTheClinicHasTaken_IsAConflict_AndChangesNothing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnMove = (_, _) => new(ClinicCrmOutcome.Taken, Message: "10:30 is not available.", FreeTimes: ["12:00", "12:15"]),
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => kit.Appointments(crm).RescheduleAsync(
            booking.Id, new RescheduleAppointmentRequest(ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 10, 30))));

        Assert.Contains("12:00, 12:15", ex.Message);
        var stored = await StoredAsync(kit, booking.Id);
        Assert.Equal(booking.StartUtc, stored.StartUtc);
        Assert.Equal(873, stored.ExternalBookingId);
    }

    [Fact]
    public async Task ReschedulingWhenTheClinicIsUnreachable_ChangesNothing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnMove = (_, _) => new(ClinicCrmOutcome.Unavailable) };

        await Assert.ThrowsAsync<BusinessRuleException>(() => kit.Appointments(crm).RescheduleAsync(
            booking.Id, new RescheduleAppointmentRequest(ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 10, 30))));

        Assert.Equal(booking.StartUtc, (await StoredAsync(kit, booking.Id)).StartUtc);
    }

    [Fact]
    public async Task ReschedulingWithTheConnectionSwitchedOff_IsRefused()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { Endpoint = null };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => kit.Appointments(crm).RescheduleAsync(
            booking.Id, new RescheduleAppointmentRequest(ClinicCrmTestKit.DubaiToUtc(day.AddDays(1), 10, 30))));

        Assert.Contains("#873", ex.Message);
        Assert.Empty(crm.Moves);
    }

    [Fact]
    public async Task Cancelling_CancelsItAtTheClinic_ThenOurs()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.Cancelled), };

        var result = await kit.Appointments(crm).CancelAsync(booking.Id, new CancelAppointmentRequest("Customer called"));

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
        Assert.Equal(ExternalSyncStatus.Synced, result.ExternalSyncStatus);
        Assert.Null(result.ExternalSyncError);
        Assert.Equal((873, "971501234567", "Customer called"), Assert.Single(crm.CancellationRequests));
    }

    [Fact]
    public async Task CancellingOneTheClinicWillNotCancel_FlagsIt_ThenCancelsOurs()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnCancel = _ => new(ClinicCrmCancelOutcome.Flagged),
        };

        var result = await kit.Appointments(crm).CancelAsync(booking.Id, new CancelAppointmentRequest("Customer called"));

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
        Assert.Equal(ExternalSyncStatus.CancelRequested, result.ExternalSyncStatus);
        Assert.Equal((873, "971501234567", "Customer called"), Assert.Single(crm.CancellationRequests));
    }

    [Fact]
    public async Task CancellingWhenTheClinicIsUnreachable_CancelsNothing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.Unavailable) };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            kit.Appointments(crm).CancelAsync(booking.Id, new CancelAppointmentRequest(null)));

        Assert.Equal("CLINIC_CRM_UNAVAILABLE", ex.ErrorCode);
        var stored = await StoredAsync(kit, booking.Id);
        Assert.Equal(AppointmentStatus.Confirmed, stored.Status);
        Assert.Equal(ExternalSyncStatus.Synced, stored.ExternalSyncStatus);
    }

    [Fact]
    public async Task CancellingABookingTheClinicNoLongerHas_StillCancelsOurs_AndSaysWhy()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.NotFound) };

        var result = await kit.Appointments(crm).CancelAsync(booking.Id, new CancelAppointmentRequest(null));

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
        Assert.Equal(ExternalSyncStatus.Synced, result.ExternalSyncStatus);
        Assert.Contains("no longer among", result.ExternalSyncError);
    }

    [Fact]
    public async Task ABookingStillWaitingToReachTheClinic_IsMovedAndCancelledHereOnly()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SeedAsync(kit, day, ExternalSyncStatus.Pending, bookingId: null);
        var crm = new FakeClinicCrmBookings(kit);   // throws on any CRM call

        var result = await kit.Appointments(crm).CancelAsync(booking.Id, new CancelAppointmentRequest(null));

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
        Assert.Empty(crm.CancellationRequests);
    }
}
