using wa_api.Features.Calendar.Entities;

namespace wa_api.Features.Calendar.Availability;

/// <inheritdoc />
public sealed class AvailabilityCalculator : IAvailabilityCalculator
{
    /// <summary>
    /// Ceiling on how many days one call may expand. The service already clamps to the type's booking
    /// horizon; this is the backstop that stops a hand-crafted date range from generating millions of
    /// slots and pinning a request thread.
    /// </summary>
    private const int MaxDays = 400;

    public IReadOnlyList<AvailableSlot> Generate(
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        TimeZoneInfo timeZone,
        IReadOnlyList<BusinessHours> businessHours,
        IReadOnlyList<BusyInterval> busy,
        SlotRules rules,
        DateTime nowUtc)
    {
        // A zero/negative duration would emit zero-length slots; a zero/negative interval would never
        // advance the cursor and spin forever. Both are configuration errors rather than states to
        // model, so they yield nothing rather than throwing into a customer-facing request.
        if (rules.DurationMinutes <= 0 || rules.SlotIntervalMinutes <= 0) return [];
        if (toLocalDate < fromLocalDate) return [];
        if (businessHours.Count == 0) return [];

        if (toLocalDate.DayNumber - fromLocalDate.DayNumber >= MaxDays)
            toLocalDate = fromLocalDate.AddDays(MaxDays - 1);

        var duration = TimeSpan.FromMinutes(rules.DurationMinutes);
        var interval = TimeSpan.FromMinutes(rules.SlotIntervalMinutes);
        var bufferBefore = TimeSpan.FromMinutes(Math.Max(0, rules.BufferBeforeMinutes));
        var bufferAfter = TimeSpan.FromMinutes(Math.Max(0, rules.BufferAfterMinutes));

        // Everything before this instant is unbookable, however open the calendar looks.
        var earliestUtc = nowUtc.AddMinutes(Math.Max(0, rules.MinimumNoticeMinutes));

        // Group once rather than filtering the whole list per date.
        var hoursByDay = businessHours
            .Where(h => h.EndLocal > h.StartLocal)   // a non-positive window is a bad row; ignore it
            .GroupBy(h => h.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.StartLocal).ToList());

        var slots = new List<AvailableSlot>();
        var seen = new HashSet<DateTime>();          // overlapping windows must not offer the same start twice

        for (var date = fromLocalDate; date <= toLocalDate; date = date.AddDays(1))
        {
            if (!hoursByDay.TryGetValue(date.DayOfWeek, out var windows)) continue;

            foreach (var window in windows)
            {
                // Unspecified kind is required: ConvertTimeToUtc rejects a Local/Utc-kinded value when
                // given an explicit source zone, and these are wall-clock times with no offset yet.
                var windowStartLocal = date.ToDateTime(window.StartLocal, DateTimeKind.Unspecified);
                var windowEndLocal = date.ToDateTime(window.EndLocal, DateTimeKind.Unspecified);

                // The fit test is in WALL-CLOCK time because business hours are a wall-clock promise:
                // "we close at 17:00" stays true through a DST change. The appointment's real length is
                // then applied in absolute time below, so a booking is always its full duration even if
                // the clocks move underneath it.
                for (var cursor = windowStartLocal; cursor + duration <= windowEndLocal; cursor += interval)
                {
                    // Spring-forward gap: this wall-clock time does not exist on this date, so no
                    // instant corresponds to it and the slot simply is not offerable.
                    if (timeZone.IsInvalidTime(cursor)) continue;

                    // Fall-back overlap resolves to standard time, matching how the same local time is
                    // rendered back to the operator. Ambiguity never throws, unlike invalid times.
                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(cursor, timeZone);

                    if (startUtc < earliestUtc) continue;
                    if (!seen.Add(startUtc)) continue;

                    var endUtc = startUtc + duration;
                    var blockStartUtc = startUtc - bufferBefore;
                    var blockEndUtc = endUtc + bufferAfter;

                    // Linear scan: a company's calendar over a booking horizon is tens of rows, so an
                    // index would cost more than it saves.
                    var blocked = false;
                    for (var i = 0; i < busy.Count; i++)
                    {
                        if (busy[i].Overlaps(blockStartUtc, blockEndUtc)) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    slots.Add(new AvailableSlot(startUtc, endUtc, blockStartUtc, blockEndUtc));
                }
            }
        }

        // Windows are ordered within a date, but a timezone whose offset shifts mid-range can emit a
        // later window before an earlier one across dates. Sorting once is cheaper than reasoning about it.
        slots.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return slots;
    }
}
