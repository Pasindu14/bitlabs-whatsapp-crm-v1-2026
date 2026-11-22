using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.ClinicCrm.Jobs;
using wa_api.Features.Conversations.Entities;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// The push for a booking the CRM could not take at the time. Either the clinic gets it, or staff are
/// told who to call — it never just stops.
/// </summary>
public class ClinicCrmSyncJobTests
{
    private static async Task<Appointment> PendingAsync(ClinicCrmTestKit kit, DateTime startUtc)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), CompanyId = kit.CompanyId, ContactId = kit.Contact.Id,
            AppointmentTypeId = kit.Service.Id, ConversationId = kit.Conversation.Id,
            StartUtc = startUtc, EndUtc = startUtc.AddMinutes(30), BlockStartUtc = startUtc, BlockEndUtc = startUtc.AddMinutes(30),
            Status = AppointmentStatus.Confirmed, Source = AppointmentSource.Bot, IsActive = true,
            ExternalSyncStatus = ExternalSyncStatus.Pending,
        };
        kit.Db.Appointments.Add(appointment);
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();
        return appointment;
    }

    private static ClinicCrmSyncJob Job(ClinicCrmTestKit kit, FakeClinicCrmBookings crm, RecordingJobs jobs) =>
        new(kit.Db, kit.Tenant, crm, jobs, NullLogger<ClinicCrmSyncJob>.Instance);

    private static DateTime InThreeDays => ClinicCrmTestKit.DubaiToUtc(ClinicCrmTestKit.DaysAhead(3), 16);

    [Fact]
    public async Task AcceptedOnRetry_BecomesSynced()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(900, ClinicCrmTestKit.DaysAhead(3), "16:00", "16:45")),
        };

        await Job(kit, crm, new RecordingJobs()).PushAsync(appointment.Id, 0, default);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalSyncStatus.Synced, stored.ExternalSyncStatus);
        Assert.Equal(900, stored.ExternalBookingId);
        Assert.Equal(appointment.StartUtc, Assert.Single(crm.Books).StartUtc);
        Assert.Empty(crm.Notifications);
    }

    [Fact]
    public async Task StillUnreachable_SchedulesTheNextAttempt_AndStaysPending()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var crm = new FakeClinicCrmBookings(kit) { OnBook = _ => new(ClinicCrmOutcome.Unavailable, Message: "down") };
        var jobs = new RecordingJobs();

        await Job(kit, crm, jobs).PushAsync(appointment.Id, 0, default);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalSyncStatus.Pending, stored.ExternalSyncStatus);
        Assert.Equal("down", stored.ExternalSyncError);

        var (job, state) = Assert.Single(jobs.Created);
        Assert.Equal(nameof(ClinicCrmSyncJob.PushAsync), job.Method.Name);
        Assert.Equal(1, job.Args[1]);                                // the next attempt
        Assert.IsType<ScheduledState>(state);
        Assert.Empty(crm.Notifications);
    }

    [Fact]
    public async Task UnreachableOnTheLastAttempt_FailsIt_FreesTheSlot_PausesTheBot_AndTellsStaff()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var crm = new FakeClinicCrmBookings(kit) { OnBook = _ => new(ClinicCrmOutcome.Unavailable) };
        var jobs = new RecordingJobs();

        await Job(kit, crm, jobs).PushAsync(appointment.Id, ClinicCrmSyncJob.RetryDelays.Count, default);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalSyncStatus.Failed, stored.ExternalSyncStatus);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);
        Assert.Empty(jobs.Created);

        var conversation = await kit.Db.Conversations.AsNoTracking().SingleAsync();
        Assert.Equal(ConversationBotState.Paused, conversation.BotState);

        var (_, body) = Assert.Single(crm.Notifications);
        Assert.Contains("Sarah Fernando", body);
        Assert.Contains("+971501234567", body);
        Assert.Contains("PRP Consultation", body);
    }

    [Fact]
    public async Task TakenByTheTimeItArrives_FailsStraightAway()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var crm = new FakeClinicCrmBookings(kit) { OnBook = _ => new(ClinicCrmOutcome.Taken) };
        var jobs = new RecordingJobs();

        await Job(kit, crm, jobs).PushAsync(appointment.Id, 0, default);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalSyncStatus.Failed, stored.ExternalSyncStatus);
        Assert.Empty(jobs.Created);
        Assert.Single(crm.Notifications);
    }

    [Fact]
    public async Task ATimeAlreadyPast_IsNotPushed()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, DateTime.UtcNow.AddHours(-1));
        var crm = new FakeClinicCrmBookings(kit);

        await Job(kit, crm, new RecordingJobs()).PushAsync(appointment.Id, 2, default);

        Assert.Empty(crm.Books);
        Assert.Equal(ExternalSyncStatus.Failed, (await kit.Db.Appointments.AsNoTracking().SingleAsync()).ExternalSyncStatus);
        Assert.Single(crm.Notifications);
    }

    [Fact]
    public async Task CancelledWhileWaiting_IsDroppedQuietly()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var tracked = await kit.Db.Appointments.SingleAsync();
        tracked.Status = AppointmentStatus.Cancelled;
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();

        var crm = new FakeClinicCrmBookings(kit);
        await Job(kit, crm, new RecordingJobs()).PushAsync(appointment.Id, 0, default);

        Assert.Empty(crm.Books);
        Assert.Empty(crm.Notifications);
        Assert.Equal(ExternalSyncStatus.NotApplicable,
            (await kit.Db.Appointments.AsNoTracking().SingleAsync()).ExternalSyncStatus);
    }

    [Fact]
    public async Task AlreadySynced_IsLeftAlone()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var appointment = await PendingAsync(kit, InThreeDays);
        var tracked = await kit.Db.Appointments.SingleAsync();
        tracked.ExternalSyncStatus = ExternalSyncStatus.Synced;
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();

        var crm = new FakeClinicCrmBookings(kit);
        await Job(kit, crm, new RecordingJobs()).PushAsync(appointment.Id, 0, default);

        Assert.Empty(crm.Books);
    }
}
