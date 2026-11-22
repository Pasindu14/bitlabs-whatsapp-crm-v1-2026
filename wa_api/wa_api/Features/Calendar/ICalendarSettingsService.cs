using wa_api.Features.Calendar.Dtos;

namespace wa_api.Features.Calendar;

/// <summary>
/// The company's calendar configuration: which timezone it operates in, when it is open, and when it is
/// closed regardless of opening hours.
/// </summary>
public interface ICalendarSettingsService
{
    /// <summary>Timezone plus the full weekly schedule — what the settings screen loads in one call.</summary>
    Task<CalendarSettingsResponse> GetAsync(CancellationToken ct = default);

    /// <summary>Sets the company's IANA timezone. Rejects an id this host cannot resolve.</summary>
    Task<CalendarSettingsResponse> UpdateTimeZoneAsync(UpdateTimeZoneRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces the ENTIRE weekly schedule with the submitted windows. Replace-all rather than per-row
    /// edits because the schedule is authored as one grid; it also makes the call idempotent.
    /// </summary>
    Task<CalendarSettingsResponse> SaveBusinessHoursAsync(SaveBusinessHoursRequest request, CancellationToken ct = default);

    /// <summary>Blackouts overlapping the given UTC window, soonest first.</summary>
    Task<IReadOnlyList<BlackoutResponse>> GetBlackoutsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Blocks a span. Rejects an end at or before the start.</summary>
    Task<BlackoutResponse> CreateBlackoutAsync(CreateBlackoutRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes a blackout, freeing the span. 404 when it isn't visible to the caller.</summary>
    Task DeleteBlackoutAsync(Guid id, CancellationToken ct = default);
}
