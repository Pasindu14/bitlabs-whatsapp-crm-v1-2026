using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.StaffAlerts;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.Conversations.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.ClinicCrm.Jobs;

/// <summary>
/// Pushes a bot booking to the clinic's CRM when the CRM could not be reached at the moment of booking.
/// <para>
/// The customer was told the clinic will confirm, so this job's whole duty is that nobody is forgotten:
/// either the CRM takes the booking, or staff are told plainly that a person has to rebook this customer.
/// It never just gives up quietly.
/// </para>
/// <para>
/// Retries are scheduled by the job itself rather than by Hangfire's <see cref="AutomaticRetryAttribute"/>,
/// so the last attempt can mark the booking failed and tell staff — Hangfire's retry has no such hook.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])]
[Queue(wa_api.Common.Extensions.HangfireExtensions.BotQueue)]
public sealed class ClinicCrmSyncJob(
    AppDbContext db,
    ScopedTenantContext tenant,
    IClinicCrmBookingService crm,
    IBackgroundJobClient jobs,
    ILogger<ClinicCrmSyncJob> logger,
    IStaffAlertService? staffAlerts = null)
{
    /// <summary>Wait before attempt n+1 (n from 0). About two hours in all.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
    ];

    public async Task PushAsync(Guid appointmentId, int attempt, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Include(a => a.Contact)
            .Include(a => a.AppointmentType)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        if (appointment is null || appointment.ExternalSyncStatus != ExternalSyncStatus.Pending) return;

        tenant.EnterCompany(appointment.CompanyId);

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            // Called off before the clinic ever heard of it. Nothing to push and nobody to tell.
            appointment.ExternalSyncStatus = ExternalSyncStatus.NotApplicable;
            appointment.ExternalSyncError = "Cancelled before it reached the clinic's system.";
            await db.SaveChangesAsync(ct);
            return;
        }

        if (appointment.StartUtc <= DateTime.UtcNow)
        {
            await FailAsync(appointment, "The appointment time passed before the clinic's system could be reached.", ct);
            return;
        }

        var endpoint = await crm.GetEndpointAsync(ct);
        if (endpoint is null)
        {
            await FailAsync(appointment, "The clinic CRM connection was switched off before this booking reached it.", ct);
            return;
        }

        var result = await crm.BookAsync(endpoint, new ClinicCrmBookingDraft(
            CustomerName(appointment.Contact.Name, appointment.Contact.Phone),
            appointment.Contact.Phone,
            appointment.AppointmentType.ExternalTreatmentId,
            appointment.StartUtc,
            appointment.Notes), ct);

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok when result.Data is { } change:
                appointment.ExternalSyncStatus = ExternalSyncStatus.Synced;
                appointment.ExternalBookingId = change.Booking.BookingId;
                appointment.ExternalCustomerId = change.Booking.CustomerId;
                appointment.ExternalSyncError = null;
                appointment.ExternalSyncedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Pushed appointment {AppointmentId} to the clinic CRM as booking {BookingId} on attempt {Attempt}.",
                    appointment.Id, change.Booking.BookingId, attempt + 1);
                return;

            case ClinicCrmOutcome.Unavailable or ClinicCrmOutcome.NotAuthorised
                when attempt < RetryDelays.Count:
                appointment.ExternalSyncError = Truncate(result.Message ?? result.Outcome.ToString());
                await db.SaveChangesAsync(ct);

                jobs.Schedule<ClinicCrmSyncJob>(
                    j => j.PushAsync(appointment.Id, attempt + 1, CancellationToken.None),
                    RetryDelays[attempt]);
                return;

            default:
                await FailAsync(appointment, ReasonFor(result), ct);
                return;
        }
    }

    /// <summary>
    /// The end of the line: free the slot we were holding, stop the bot on the thread so it cannot tell
    /// the customer anything else about this booking, and tell staff exactly who to call.
    /// </summary>
    private async Task FailAsync(Appointment appointment, string reason, CancellationToken ct)
    {
        appointment.ExternalSyncStatus = ExternalSyncStatus.Failed;
        appointment.ExternalSyncError = Truncate(reason);
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.CancellationReason = Truncate($"Not accepted by the clinic's system: {reason}");
        appointment.HoldExpiresAt = null;

        if (appointment.ConversationId is { } conversationId
            && await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct) is { } conversation
            && conversation.BotState != ConversationBotState.Paused)
        {
            conversation.BotState = ConversationBotState.Paused;
            conversation.BotPausedAt = DateTime.UtcNow;
            conversation.BotPausedByUserId = null;
            conversation.BotResumesAt = null;
        }

        await db.SaveChangesAsync(ct);

        var timeZoneId = await db.Companies.AsNoTracking()
            .Where(c => c.Id == appointment.CompanyId)
            .Select(c => c.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        var local = TimeZoneInfo.ConvertTimeFromUtc(
            appointment.StartUtc, CompanyTimeZone.ResolveOrFallback(timeZoneId));

        logger.LogWarning(
            "Appointment {AppointmentId} could not be pushed to the clinic CRM: {Reason}", appointment.Id, reason);

        await crm.NotifyStaffAsync(
            appointment.CompanyId,
            "Booking did not reach the clinic CRM",
            $"{CustomerName(appointment.Contact.Name, appointment.Contact.Phone)} (+{appointment.Contact.Phone}) "
            + $"asked for {appointment.AppointmentType.Name} on "
            + $"{local.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture)}. {reason} "
            + "They were told the clinic would confirm — please contact them to rebook. The assistant is paused "
            + "on this chat.",
            ct);

        // The in-app note above carries the detail; the team's phones get the short version.
        if (staffAlerts is not null)
            await staffAlerts.AlertAsync(new StaffAlert(
                appointment.CompanyId, appointment.ContactId, appointment.ConversationId,
                $"Booking for {appointment.AppointmentType.Name} on "
                + $"{local.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture)} did not reach the clinic. "
                + "Please contact them to rebook.",
                InApp: false), ct);
    }

    private static string ReasonFor<T>(ClinicCrmResult<T> result) => result.Outcome switch
    {
        ClinicCrmOutcome.Taken => "That time is no longer free at the clinic.",
        ClinicCrmOutcome.NotAuthorised => "The clinic's system did not accept our credentials.",
        ClinicCrmOutcome.Unavailable => "The clinic's system could not be reached after several tries.",
        _ => result.Message ?? "The clinic's system refused the booking.",
    };

    private static string CustomerName(string? name, string phone) =>
        string.IsNullOrWhiteSpace(name) ? $"+{phone}" : name.Trim();

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}
