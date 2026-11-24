using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using wa_api.Features.Calendar;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.ClinicCrm.Jobs;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.ClinicCrm;

/// <inheritdoc />
public sealed class ClinicCrmBookingService(
    AppDbContext db,
    IClinicCrmClient client,
    IOptions<ClinicCrmOptions> options,
    IBackgroundJobClient jobs,
    INotificationService notifications,
    ILogger<ClinicCrmBookingService> logger,
    IClinicCrmChatSummarizer? summarizer = null) : IClinicCrmBookingService
{
    private const int MaxNameLength = 150;
    private const int MaxNotesLength = 2000;
    private const int MaxCancelNoteLength = 500;

    /// <summary>A healthy connection is re-stamped at most this often, so a busy day is not a write per call.</summary>
    private static readonly TimeSpan SuccessStampInterval = TimeSpan.FromMinutes(1);

    public TimeZoneInfo CrmTimeZone { get; } = CompanyTimeZone.ResolveOrFallback(options.Value.TimeZoneId);

    public async Task<ClinicCrmEndpoint?> GetEndpointAsync(CancellationToken ct = default)
    {
        var connection = await db.ClinicCrmConnections.AsNoTracking()
            .Where(c => c.IsActive && c.IsEnabled)
            .Select(c => new { c.Id, c.BaseUrl, c.ClientId, c.ClientSecret })
            .FirstOrDefaultAsync(ct);

        return connection is null || string.IsNullOrWhiteSpace(connection.ClientSecret)
            ? null
            : new ClinicCrmEndpoint(connection.Id, connection.BaseUrl, connection.ClientId, connection.ClientSecret);
    }

    public async Task<ClinicCrmEndpoint?> GetMessageNotifyEndpointAsync(CancellationToken ct = default)
    {
        var connection = await db.ClinicCrmConnections.AsNoTracking()
            .Where(c => c.IsActive && c.IsEnabled && c.NotifyOnMessages)
            .Select(c => new { c.Id, c.BaseUrl, c.ClientId, c.ClientSecret })
            .FirstOrDefaultAsync(ct);

        return connection is null || string.IsNullOrWhiteSpace(connection.ClientSecret)
            ? null
            : new ClinicCrmEndpoint(connection.Id, connection.BaseUrl, connection.ClientId, connection.ClientSecret);
    }

    public async Task<ClinicCrmResult<JsonElement>> NotifyMessageAsync(
        ClinicCrmEndpoint endpoint, string phone, CancellationToken ct = default) =>
        await TrackAsync(endpoint,
            await client.NotifyMessageAsync(endpoint, new ClinicCrmMessageRequest(ClinicCrmFormat.WhatsApp(phone)), ct),
            ct);

    public async Task<string?> SummarizeConversationAsync(
        Guid companyId, Guid conversationId, string serviceName, CancellationToken ct = default)
    {
        if (summarizer is null) return null;

        try
        {
            return await summarizer.SummarizeAsync(companyId, conversationId, serviceName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat summary failed for conversation {ConversationId}; booking without it.",
                conversationId);
            return null;
        }
    }

    public async Task<ClinicCrmResult<ClinicCrmCustomer>> SaveInterestedCustomerAsync(
        ClinicCrmEndpoint endpoint, string name, string phone, CancellationToken ct = default) =>
        await SaveCustomerAsync(
            endpoint, Truncate(name.Trim(), MaxNameLength), ClinicCrmFormat.WhatsApp(phone), treatmentId: null, ct);

    public async Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
        ClinicCrmEndpoint endpoint, DateOnly date, CancellationToken ct = default) =>
        await TrackAsync(endpoint, await client.GetAvailabilityAsync(endpoint, date, ct), ct);

    public async Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmBookingDraft draft, CancellationToken ct = default)
    {
        var phone = ClinicCrmFormat.WhatsApp(draft.Phone);
        var (date, time) = ClinicCrmFormat.FromUtc(draft.StartUtc, CrmTimeZone);
        var name = Truncate(draft.CustomerName.Trim(), MaxNameLength);

        var saved = await SaveCustomerAsync(endpoint, name, phone, draft.TreatmentId, ct);
        if (!saved.IsOk) return saved.As<ClinicCrmBookingChange>();

        var request = new ClinicCrmBookRequest(
            phone, TreatmentIds(draft.TreatmentId), date, time,
            TruncateOrNull(WithNameGiven(name, saved.Data?.CustomerName, draft.Notes), MaxNotesLength));

        var booked = await TrackAsync(endpoint, await client.BookAsync(endpoint, request, ct), ct);

        // "Customer not saved yet" straight after saving them: save once more and book once more.
        if (booked.Outcome == ClinicCrmOutcome.NotFound)
        {
            saved = await SaveCustomerAsync(endpoint, name, phone, draft.TreatmentId, ct);
            if (!saved.IsOk) return saved.As<ClinicCrmBookingChange>();

            booked = await TrackAsync(endpoint, await client.BookAsync(endpoint, request, ct), ct);
        }

        return booked;
    }

    public async Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetUpcomingAsync(
        ClinicCrmEndpoint endpoint, string phone, CancellationToken ct = default) =>
        await TrackAsync(endpoint, await client.GetBookingsAsync(endpoint, ClinicCrmFormat.WhatsApp(phone), ct), ct);

    public async Task<ClinicCrmResult<ClinicCrmBookingChange>> MoveAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, DateTime newStartUtc, CancellationToken ct = default)
    {
        var (date, time) = ClinicCrmFormat.FromUtc(newStartUtc, CrmTimeZone);
        var request = new ClinicCrmUpdateBookingRequest(ClinicCrmFormat.WhatsApp(phone), date, time);

        return await TrackAsync(endpoint, await client.UpdateBookingAsync(endpoint, bookingId, request, ct), ct);
    }

    public async Task<ClinicCrmCancelResult> CancelBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default)
    {
        var request = new ClinicCrmCancelBookingRequest(
            ClinicCrmFormat.WhatsApp(phone), TruncateOrNull(reason, MaxCancelNoteLength));

        var cancelled = await TrackAsync(endpoint, await client.CancelBookingAsync(endpoint, bookingId, request, ct), ct);

        switch (cancelled.Outcome)
        {
            case ClinicCrmOutcome.Ok:
                return new(ClinicCrmCancelOutcome.Cancelled, cancelled.Message);

            case ClinicCrmOutcome.NotFound:
                return new(ClinicCrmCancelOutcome.NotFound, cancelled.Message);

            case ClinicCrmOutcome.Unavailable or ClinicCrmOutcome.NotAuthorised:
                return new(ClinicCrmCancelOutcome.Unavailable, cancelled.Message);
        }

        // 400/409: the CRM will not cancel it (e.g. too close to the time). Leave it for a person there.
        logger.LogInformation(
            "Clinic CRM would not cancel booking {BookingId} ({Outcome}: {Message}); flagging it instead.",
            bookingId, cancelled.Outcome, cancelled.Message);

        var flagged = await RequestCancellationAsync(endpoint, bookingId, phone, reason, ct);
        return flagged.Outcome switch
        {
            ClinicCrmOutcome.Ok => new(ClinicCrmCancelOutcome.Flagged, cancelled.Message),
            ClinicCrmOutcome.NotFound => new(ClinicCrmCancelOutcome.NotFound, flagged.Message),
            _ => new(ClinicCrmCancelOutcome.Unavailable, flagged.Message),
        };
    }

    /// <summary>How a cancellation flag starts, so a second request does not stack another one on top.</summary>
    public const string CancellationFlag = "CANCELLATION REQUESTED";

    public async Task<ClinicCrmResult<ClinicCrmBookingChange>> RequestCancellationAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default)
    {
        var whatsapp = ClinicCrmFormat.WhatsApp(phone);

        // Read first: PATCH replaces the note, and the clinic may have written its own on the booking.
        var upcoming = await TrackAsync(endpoint, await client.GetBookingsAsync(endpoint, whatsapp, ct), ct);
        if (!upcoming.IsOk) return upcoming.As<ClinicCrmBookingChange>();

        var booking = upcoming.Data?.FirstOrDefault(b => b.BookingId == bookingId);
        if (booking is null)
            return new(ClinicCrmOutcome.NotFound,
                Message: $"Booking #{bookingId} is not among the customer's upcoming clinic bookings.");

        if (booking.Notes?.StartsWith(CancellationFlag, StringComparison.Ordinal) == true)
            return new(ClinicCrmOutcome.Ok, new ClinicCrmBookingChange("updated", null, booking));

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CrmTimeZone);
        var flag = $"{CancellationFlag} {now:yyyy-MM-dd HH:mm} via the WhatsApp assistant"
            + (string.IsNullOrWhiteSpace(reason) ? "" : $" — reason: {reason.Trim()}")
            + ". Please cancel this booking in the CRM.";
        var notes = string.IsNullOrWhiteSpace(booking.Notes) ? flag : $"{flag}\n\n{booking.Notes}";

        return await TrackAsync(endpoint,
            await client.UpdateBookingAsync(endpoint, bookingId,
                new ClinicCrmUpdateBookingRequest(whatsapp, Notes: Truncate(notes, MaxNotesLength)), ct),
            ct);
    }

    public void QueueSync(Guid appointmentId) =>
        jobs.Enqueue<ClinicCrmSyncJob>(j => j.PushAsync(appointmentId, 0, CancellationToken.None));

    public async Task NotifyStaffAsync(Guid companyId, string title, string body, CancellationToken ct = default)
    {
        try
        {
            await notifications.CreateAsync(companyId, NotificationType.System, title, body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The log is the fallback record. A failed notification must not undo or block the booking work.
            logger.LogError(ex, "Could not notify staff of company {CompanyId}: {Title} — {Body}", companyId, title, body);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<ClinicCrmResult<ClinicCrmCustomer>> SaveCustomerAsync(
        ClinicCrmEndpoint endpoint, string name, string phone, int? treatmentId, CancellationToken ct) =>
        await TrackAsync(endpoint,
            await client.SaveCustomerAsync(
                endpoint, new ClinicCrmSaveCustomerRequest(name, phone, TreatmentIds(treatmentId), Notes: null), ct),
            ct);

    /// <summary>
    /// The CRM keys customers by number and keeps the name it saw first: observed live, "Charlie" booked
    /// and the calendar showed "dermalyn" from an older chat on that number. Its API cannot rename a
    /// customer, so the name given for this booking goes first in the notes, where the clinic sees it.
    /// </summary>
    public static string? WithNameGiven(string nameGiven, string? nameOnFile, string? notes)
    {
        if (string.IsNullOrWhiteSpace(nameOnFile)
            || string.Equals(nameOnFile.Trim(), nameGiven.Trim(), StringComparison.OrdinalIgnoreCase))
            return notes;

        var line = $"Name given for this booking: {nameGiven.Trim()} (on file as {nameOnFile.Trim()})";
        return string.IsNullOrWhiteSpace(notes) ? line : line + "\n" + notes.Trim();
    }

    /// <summary>The CRM accepts an empty list: a customer or booking whose treatment is not known.</summary>
    private static IReadOnlyList<int> TreatmentIds(int? treatmentId) => treatmentId is { } id ? [id] : [];

    /// <summary>
    /// Records the connection's health for the settings screen. Only failures an admin can act on are
    /// errors — "that time is taken" is the CRM working as intended.
    /// </summary>
    private async Task<ClinicCrmResult<T>> TrackAsync<T>(
        ClinicCrmEndpoint endpoint, ClinicCrmResult<T> result, CancellationToken ct)
    {
        try
        {
            var connection = await db.ClinicCrmConnections.FirstOrDefaultAsync(c => c.Id == endpoint.ConnectionId, ct);
            if (connection is null) return result;

            var now = DateTime.UtcNow;
            var broken = result.Outcome is ClinicCrmOutcome.NotAuthorised or ClinicCrmOutcome.Unavailable;

            if (broken)
            {
                connection.LastError = Truncate(result.Message ?? result.Outcome.ToString(), 500);
                connection.LastErrorAt = now;
            }
            else if (connection.LastError is not null
                     || connection.LastSuccessAt is null
                     || now - connection.LastSuccessAt > SuccessStampInterval)
            {
                connection.LastSuccessAt = now;
                connection.LastError = null;
                connection.LastErrorAt = null;
            }
            else
            {
                return result;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record clinic CRM health for connection {ConnectionId}", endpoint.ConnectionId);
        }

        return result;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private static string? TruncateOrNull(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : Truncate(text.Trim(), max);
}
