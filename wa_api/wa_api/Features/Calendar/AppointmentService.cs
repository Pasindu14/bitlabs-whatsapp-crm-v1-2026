using Microsoft.EntityFrameworkCore;
using Npgsql;
using wa_api.Common.Errors;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Calendar;

/// <inheritdoc />
/// <remarks>
/// A booking the clinic's CRM holds (plan 014: <see cref="ExternalSyncStatus.Synced"/>) is moved and
/// cancelled through the CRM first, whoever asks — staff on the calendar or the bot — so the two calendars
/// never disagree. When the CRM cannot be reached, nothing changes here either.
/// </remarks>
public class AppointmentService(
    AppDbContext db,
    IAvailabilityService availability,
    ILogger<AppointmentService> logger,
    IClinicCrmBookingService? crm = null) : IAppointmentService
{
    /// <summary>
    /// Postgres <c>exclusion_violation</c>. Raised when the overlap constraint rejects a booking that
    /// would sit on top of an existing one — the losing side of a concurrent confirmation.
    /// </summary>
    private const string ExclusionViolation = "23P01";

    public async Task<IReadOnlyList<AppointmentResponse>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, bool includeCancelled = false, CancellationToken ct = default)
    {
        var from = CompanyTimeZone.AsUtc(fromUtc);
        var to = CompanyTimeZone.AsUtc(toUtc);

        // The global query filter already scopes this to the caller's company.
        var query = db.Appointments.AsNoTracking()
            .Include(a => a.Contact)
            .Include(a => a.AppointmentType)
            // Half-open overlap on the customer-facing range: what the grid draws is the appointment,
            // not its buffers.
            .Where(a => a.StartUtc < to && from < a.EndUtc);

        if (!includeCancelled)
            query = query.Where(a => a.Status != AppointmentStatus.Cancelled);

        var rows = await query.OrderBy(a => a.StartUtc).ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<AppointmentResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(id, tracked: false, ct);
        return Map(appointment);
    }

    public async Task<AppointmentResponse> CreateAsync(
        CreateAppointmentRequest request, AppointmentSource source, Guid? createdByUserId,
        CancellationToken ct = default)
    {
        var start = CompanyTimeZone.AsUtc(request.StartUtc);

        var type = await db.AppointmentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.AppointmentTypeId, ct)
            ?? throw new NotFoundException("AppointmentType", request.AppointmentTypeId);

        if (!type.IsActive || !type.IsEnabled)
            throw new BusinessRuleException("APPOINTMENT_TYPE_DISABLED",
                "This appointment type is not currently bookable.");

        // Query-filter scoped, so a contact from another company is a clean 404 rather than a leak.
        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new NotFoundException("Contact", request.ContactId);

        // A bot booking is made on the customer's behalf, so it must land on a slot we would genuinely
        // have offered — inside opening hours, past the notice period, clear of blackouts. Staff booking
        // directly are trusted: a walk-in or an after-hours favour is legitimate, and the overlap
        // constraint below still prevents the one outcome that is never acceptable.
        if (source == AppointmentSource.Bot && !await availability.IsOfferableAsync(type.Id, start, ct))
            throw new BusinessRuleException("SLOT_NOT_AVAILABLE",
                "That time is no longer available. Please choose another slot.");

        var appointment = new Appointment
        {
            ContactId = contact.Id,
            AppointmentTypeId = type.Id,
            ConversationId = request.ConversationId,
            Status = AppointmentStatus.Confirmed,
            Source = source,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = createdByUserId,
            // CompanyId is auto-stamped from tenant context by AuditInterceptor on insert.
        };
        ApplyTiming(appointment, start, type);

        db.Appointments.Add(appointment);
        await SaveOrConflictAsync(ct);

        logger.LogInformation(
            "Booked appointment {AppointmentId} for contact {ContactId} at {StartUtc} via {Source}.",
            appointment.Id, contact.Id, appointment.StartUtc, source);

        return await GetByIdAsync(appointment.Id, ct);
    }

    public async Task<AppointmentResponse> CreateMirrorAsync(
        CreateAppointmentRequest request, DateTime? endUtc, ExternalBookingLink link, CancellationToken ct = default)
    {
        var start = CompanyTimeZone.AsUtc(request.StartUtc);

        var type = await db.AppointmentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.AppointmentTypeId, ct)
            ?? throw new NotFoundException("AppointmentType", request.AppointmentTypeId);

        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new NotFoundException("Contact", request.ContactId);

        var appointment = new Appointment
        {
            ContactId = contact.Id,
            AppointmentTypeId = type.Id,
            ConversationId = request.ConversationId,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Bot,
            Notes = request.Notes?.Trim(),
            ExternalSyncStatus = link.Status,
            ExternalBookingId = link.BookingId,
            ExternalCustomerId = link.CustomerId,
            ExternalSyncError = link.Error,
            ExternalSyncedAt = link.Status == ExternalSyncStatus.Synced ? DateTime.UtcNow : null,
        };
        ApplyTiming(appointment, start, type, endUtc is { } end ? CompanyTimeZone.AsUtc(end) : null);

        db.Appointments.Add(appointment);
        await SaveOrConflictAsync(ct);

        logger.LogInformation(
            "Mirrored clinic CRM booking {ExternalBookingId} as appointment {AppointmentId} ({SyncStatus}).",
            link.BookingId, appointment.Id, link.Status);

        return await GetByIdAsync(appointment.Id, ct);
    }

    public async Task<AppointmentResponse> ApplyExternalMoveAsync(
        Guid id, DateTime startUtc, DateTime? endUtc, int externalBookingId, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(id, tracked: true, ct);

        var type = await db.AppointmentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == appointment.AppointmentTypeId, ct)
            ?? throw new NotFoundException("AppointmentType", appointment.AppointmentTypeId);

        ApplyExternalMove(appointment, type, CompanyTimeZone.AsUtc(startUtc),
            endUtc is { } end ? CompanyTimeZone.AsUtc(end) : null, externalBookingId);

        await SaveOrConflictAsync(ct);
        return await GetByIdAsync(appointment.Id, ct);
    }

    private static void ApplyExternalMove(
        Appointment appointment, AppointmentType type, DateTime startUtc, DateTime? endUtc, int externalBookingId)
    {
        ApplyTiming(appointment, startUtc, type, endUtc);

        appointment.ExternalBookingId = externalBookingId;
        appointment.ExternalSyncStatus = ExternalSyncStatus.Synced;
        appointment.ExternalSyncError = null;
        appointment.ExternalSyncedAt = DateTime.UtcNow;
        // A reminder sent for the old time says nothing true about the new one.
        appointment.ReminderSentAt = null;
    }

    /// <summary>
    /// Moves a CRM booking in the CRM, then here. The CRM applies its own rules and books the new time under a
    /// new id; every refusal leaves this row exactly as it was.
    /// </summary>
    private async Task<AppointmentResponse> MoveAtClinicAsync(
        Appointment appointment, AppointmentType type, int bookingId, DateTime startUtc, CancellationToken ct)
    {
        var endpoint = await crm!.GetEndpointAsync(ct)
            ?? throw new BusinessRuleException("CLINIC_CRM_OFF",
                $"This booking is in the clinic's CRM (#{bookingId}) and the connection is switched off. "
                + "Move it in the clinic's CRM instead.");

        var result = await crm.MoveAsync(endpoint, bookingId, appointment.Contact.Phone, startUtc, ct);

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok when result.Data is { } change:
                var endUtc = ClinicCrmFormat.ToUtc(change.Booking.Date, change.Booking.EndTime, crm.CrmTimeZone);
                ApplyExternalMove(appointment, type, startUtc, endUtc, change.Booking.BookingId);
                await SaveOrConflictAsync(ct);

                logger.LogInformation(
                    "Moved clinic CRM booking {OldBookingId} to {NewBookingId} (appointment {AppointmentId}).",
                    bookingId, change.Booking.BookingId, appointment.Id);
                return await GetByIdAsync(appointment.Id, ct);

            case ClinicCrmOutcome.Taken:
                throw new ConflictException("SLOT_TAKEN",
                    (result.Message ?? "That time is not free at the clinic.")
                    + (result.FreeTimes is { Count: > 0 } free
                        ? $" Still free that day (clinic time): {string.Join(", ", free.Take(8))}."
                        : " Choose another slot."));

            case ClinicCrmOutcome.Rejected:
                throw new BusinessRuleException("CLINIC_CRM_REJECTED",
                    result.Message ?? "The clinic's system refused that time.");

            case ClinicCrmOutcome.NotFound:
                throw new BusinessRuleException("CLINIC_CRM_BOOKING_GONE",
                    $"The clinic's CRM no longer has booking #{bookingId} for this customer — it may have been "
                    + "cancelled or changed there.");

            default:
                throw new BusinessRuleException("CLINIC_CRM_UNAVAILABLE",
                    "The clinic's system could not be reached, so the booking was not moved. Try again shortly.");
        }
    }

    public async Task<AppointmentResponse> RescheduleAsync(
        Guid id, RescheduleAppointmentRequest request, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(id, tracked: true, ct);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException("APPOINTMENT_CANCELLED",
                "A cancelled appointment cannot be rescheduled. Create a new booking instead.");

        var type = await db.AppointmentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == appointment.AppointmentTypeId, ct)
            ?? throw new NotFoundException("AppointmentType", appointment.AppointmentTypeId);

        if (crm is not null
            && appointment.ExternalSyncStatus == ExternalSyncStatus.Synced
            && appointment.ExternalBookingId is { } bookingId)
            return await MoveAtClinicAsync(appointment, type, bookingId, CompanyTimeZone.AsUtc(request.StartUtc), ct);

        // Rebooked under CURRENT rules, not the ones in force when it was first taken: a reschedule is a
        // fresh decision about a future time, so today's duration and buffers are the honest ones to apply.
        ApplyTiming(appointment, CompanyTimeZone.AsUtc(request.StartUtc), type);

        await SaveOrConflictAsync(ct);
        return await GetByIdAsync(appointment.Id, ct);
    }

    public async Task<AppointmentResponse> CancelAsync(
        Guid id, CancelAppointmentRequest request, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(id, tracked: true, ct);

        if (appointment.Status == AppointmentStatus.Cancelled)
            return Map(appointment);   // idempotent

        if (crm is not null
            && appointment.ExternalSyncStatus == ExternalSyncStatus.Synced
            && appointment.ExternalBookingId is { } bookingId)
            await RequestClinicCancellationAsync(appointment, bookingId, request.Reason, ct);

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.CancellationReason = request.Reason?.Trim();
        appointment.HoldExpiresAt = null;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(appointment.Id, ct);
    }

    public async Task<AppointmentResponse> UpdateStatusAsync(
        Guid id, UpdateAppointmentStatusRequest request, CancellationToken ct = default)
    {
        // Cancelling frees a slot and records a reason, so it goes through its own endpoint rather than
        // arriving as an incidental status edit. Pending is set only by the bot taking a hold.
        if (request.Status is AppointmentStatus.Cancelled or AppointmentStatus.Pending)
            throw new BusinessRuleException("APPOINTMENT_STATUS_INVALID",
                "Use the cancel endpoint to cancel a booking. Holds are created by the chatbot only.");

        var appointment = await LoadAsync(id, tracked: true, ct);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException("APPOINTMENT_CANCELLED",
                "A cancelled appointment cannot change status.");

        appointment.Status = request.Status;
        // Confirming a hold makes it permanent — the sweeper must stop treating it as expirable.
        appointment.HoldExpiresAt = null;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(appointment.Id, ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets both ranges from a start plus the type's rules. The customer-facing range is what they are
    /// told; the block range is what the calendar actually reserves, and the two are always written
    /// together so they can never drift apart.
    /// </summary>
    /// <summary>
    /// Cancels the CRM booking first. If the CRM will not cancel it, it is flagged for a person there
    /// (<see cref="ExternalSyncStatus.CancelRequested"/>). Throws — and so cancels nothing here — when the CRM
    /// cannot be reached: a booking cancelled on one calendar and live on the other is the state this
    /// integration exists to prevent.
    /// </summary>
    private async Task RequestClinicCancellationAsync(
        Appointment appointment, int bookingId, string? reason, CancellationToken ct)
    {
        var endpoint = await crm!.GetEndpointAsync(ct);
        if (endpoint is null)
        {
            appointment.ExternalSyncError =
                $"Cancelled here while the clinic CRM connection was off — cancel booking #{bookingId} in the CRM.";
            return;
        }

        var result = await crm.CancelBookingAsync(endpoint, bookingId, appointment.Contact.Phone, reason, ct);

        switch (result.Outcome)
        {
            case ClinicCrmCancelOutcome.Cancelled:
                // Cancelled on both calendars: still in step.
                appointment.ExternalSyncError = null;
                appointment.ExternalSyncedAt = DateTime.UtcNow;
                break;

            case ClinicCrmCancelOutcome.Flagged:
                appointment.ExternalSyncStatus = ExternalSyncStatus.CancelRequested;
                appointment.ExternalSyncError = result.Message is null
                    ? null
                    : $"The clinic's system would not cancel it ({result.Message}), so it is flagged for them to cancel.";
                break;

            case ClinicCrmCancelOutcome.NotFound:
                // Not among their upcoming bookings: the clinic already cancelled or moved it.
                appointment.ExternalSyncError =
                    $"Booking #{bookingId} was no longer among the customer's upcoming clinic bookings.";
                break;

            default:
                throw new BusinessRuleException("CLINIC_CRM_UNAVAILABLE",
                    "The clinic's system could not be reached, so nothing was cancelled. Try again shortly, or "
                    + $"cancel booking #{bookingId} in the clinic's CRM.");
        }
    }

    /// <param name="endUtc">
    /// An end decided elsewhere — the clinic CRM sets its own booking length. Ignored unless after the start.
    /// </param>
    private static void ApplyTiming(
        Appointment appointment, DateTime startUtc, AppointmentType type, DateTime? endUtc = null)
    {
        appointment.StartUtc = startUtc;
        appointment.EndUtc = endUtc is { } end && end > startUtc ? end : startUtc.AddMinutes(type.DurationMinutes);
        appointment.BlockStartUtc = appointment.StartUtc.AddMinutes(-type.BufferBeforeMinutes);
        appointment.BlockEndUtc = appointment.EndUtc.AddMinutes(type.BufferAfterMinutes);
    }

    /// <summary>
    /// Commits, translating the database's overlap rejection into a clean domain conflict.
    /// <para>
    /// This is the only correct place to detect a double booking. Checking for a clash before inserting
    /// cannot be made safe — two bot conversations confirming the same slot both read "free" and both
    /// then write. The exclusion constraint arbitrates, and the loser lands here.
    /// </para>
    /// </summary>
    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ExclusionViolation })
        {
            throw new ConflictException("SLOT_TAKEN",
                "That time has just been booked by someone else. Please choose another slot.");
        }
    }

    private async Task<Appointment> LoadAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var query = db.Appointments
            .Include(a => a.Contact)
            .Include(a => a.AppointmentType)
            .AsQueryable();

        if (!tracked) query = query.AsNoTracking();

        // Query-filter scoped, so an appointment from another company is a clean 404.
        return await query.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Appointment", id);
    }

    private static AppointmentResponse Map(Appointment a) => new(
        a.Id, a.ContactId, a.Contact.Name, a.Contact.Phone,
        a.AppointmentTypeId, a.AppointmentType.Name, a.ConversationId,
        a.StartUtc, a.EndUtc, a.Status, a.Source, a.Notes,
        a.HoldExpiresAt, a.CancelledAt, a.CancellationReason, a.CreatedAt,
        a.ExternalSyncStatus, a.ExternalBookingId, a.ExternalSyncError);
}
