using wa_api.Features.Calendar.Dtos;

namespace wa_api.Features.Calendar;

/// <summary>
/// Answers "when can this be booked?" by feeding the company's configuration and current bookings into
/// <see cref="Availability.IAvailabilityCalculator"/>.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Bookable slots for an appointment type. Both dates are in the company's local calendar and are
    /// clamped to the type's booking horizon, so an unbounded range cannot be requested.
    /// </summary>
    /// <param name="appointmentTypeId">The service being booked.</param>
    /// <param name="fromDate">First local date; defaults to today in the company's zone.</param>
    /// <param name="toDate">Last local date; defaults to the end of the type's horizon.</param>
    Task<AvailabilityResponse> GetAsync(
        Guid appointmentTypeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    /// <summary>
    /// True when <paramref name="startUtc"/> is a slot this company would actually have offered — inside
    /// opening hours, clear of blackouts and bookings, and past the minimum notice.
    /// <para>
    /// This is the gate for bookings made ON the customer's behalf by the bot. Staff booking directly on
    /// the calendar bypass it deliberately: a walk-in or an after-hours favour is legitimate, and the
    /// database's overlap constraint still prevents the one thing that must never happen.
    /// </para>
    /// </summary>
    Task<bool> IsOfferableAsync(Guid appointmentTypeId, DateTime startUtc, CancellationToken ct = default);
}
