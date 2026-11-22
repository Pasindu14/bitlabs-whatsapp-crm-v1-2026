using wa_api.Features.Calendar.Entities;

namespace wa_api.Features.Calendar.Availability;

/// <summary>
/// Turns opening hours plus everything already occupying the calendar into the list of slots a customer
/// may actually book. Pure — no database, no clock, no timezone lookup of its own — so every rule it
/// encodes is settled by unit tests rather than against live data.
/// </summary>
public interface IAvailabilityCalculator
{
    /// <summary>
    /// Slots between <paramref name="fromLocalDate"/> and <paramref name="toLocalDate"/> inclusive,
    /// ordered by start and free of duplicates.
    /// </summary>
    /// <param name="fromLocalDate">First date to consider, in the company's local calendar.</param>
    /// <param name="toLocalDate">Last date to consider, inclusive.</param>
    /// <param name="timeZone">The company's timezone — the only thing that can turn a local rule into an instant.</param>
    /// <param name="businessHours">All opening windows; rows for other weekdays are ignored per date.</param>
    /// <param name="busy">Everything already occupying the calendar: appointments (buffers included) and blackouts.</param>
    /// <param name="rules">Duration, interval, buffers and minimum notice for the type being booked.</param>
    /// <param name="nowUtc">Current instant, injected so "minimum notice" is testable.</param>
    IReadOnlyList<AvailableSlot> Generate(
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        TimeZoneInfo timeZone,
        IReadOnlyList<BusinessHours> businessHours,
        IReadOnlyList<BusyInterval> busy,
        SlotRules rules,
        DateTime nowUtc);
}
