using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;

namespace wa_api.Features.Calendar;

/// <summary>Reads and writes bookings on the company's shared calendar.</summary>
public interface IAppointmentService
{
    /// <summary>
    /// Appointments overlapping a UTC window — what the calendar grid renders. Cancelled rows are
    /// excluded unless <paramref name="includeCancelled"/> is set.
    /// </summary>
    Task<IReadOnlyList<AppointmentResponse>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, bool includeCancelled = false, CancellationToken ct = default);

    /// <summary>Single appointment; 404 when it isn't visible to the caller.</summary>
    Task<AppointmentResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Books a slot. The end and the occupied range are derived server-side from the appointment type,
    /// so only the start is taken from the caller.
    /// <para>
    /// A <see cref="AppointmentSource.Bot"/> booking must land on a slot the company would actually have
    /// offered; a staff booking is trusted and only has to clear the database's overlap constraint.
    /// Either way a losing race surfaces as a SLOT_TAKEN conflict rather than a double booking.
    /// </para>
    /// </summary>
    Task<AppointmentResponse> CreateAsync(
        CreateAppointmentRequest request, AppointmentSource source, Guid? createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Records a bot booking whose fate the clinic's CRM decides (plan 014): one the CRM has accepted, or
    /// one waiting to be pushed to it. Our own offerability rules are NOT applied — the CRM's calendar is
    /// the one that counts — but the overlap constraint still is, so a clash surfaces as SLOT_TAKEN.
    /// </summary>
    /// <param name="endUtc">The CRM's end time when it gave one; otherwise derived from the service.</param>
    Task<AppointmentResponse> CreateMirrorAsync(
        CreateAppointmentRequest request, DateTime? endUtc, ExternalBookingLink link,
        CancellationToken ct = default);

    /// <summary>Applies a move the CRM has accepted, including the CRM's new booking id.</summary>
    Task<AppointmentResponse> ApplyExternalMoveAsync(
        Guid id, DateTime startUtc, DateTime? endUtc, int externalBookingId, CancellationToken ct = default);

    /// <summary>Moves a booking to a new start, recomputing its length and buffers under current rules.</summary>
    Task<AppointmentResponse> RescheduleAsync(
        Guid id, RescheduleAppointmentRequest request, CancellationToken ct = default);

    /// <summary>Cancels a booking, freeing the slot. Idempotent.</summary>
    Task<AppointmentResponse> CancelAsync(
        Guid id, CancelAppointmentRequest request, CancellationToken ct = default);

    /// <summary>Records the attendance outcome (confirmed / completed / no-show).</summary>
    Task<AppointmentResponse> UpdateStatusAsync(
        Guid id, UpdateAppointmentStatusRequest request, CancellationToken ct = default);
}
