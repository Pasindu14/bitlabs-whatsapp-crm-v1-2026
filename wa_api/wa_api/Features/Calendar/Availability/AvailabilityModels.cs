namespace wa_api.Features.Calendar.Availability;

/// <summary>
/// The booking rules the calculator needs, lifted off <c>AppointmentType</c> so the calculator depends
/// on a plain value rather than an entity. Keeps it trivially constructible in tests.
/// </summary>
/// <param name="DurationMinutes">Length of the appointment the customer is told about.</param>
/// <param name="SlotIntervalMinutes">How often a slot may start within an opening window.</param>
/// <param name="BufferBeforeMinutes">Dead time reserved before the appointment.</param>
/// <param name="BufferAfterMinutes">Dead time reserved after the appointment.</param>
/// <param name="MinimumNoticeMinutes">Earliest bookable slot, as minutes from now. 0 disables.</param>
public readonly record struct SlotRules(
    int DurationMinutes,
    int SlotIntervalMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int MinimumNoticeMinutes);

/// <summary>A half-open UTC interval [Start, End) that something already occupies.</summary>
/// <param name="StartUtc">Inclusive start.</param>
/// <param name="EndUtc">Exclusive end.</param>
public readonly record struct BusyInterval(DateTime StartUtc, DateTime EndUtc)
{
    /// <summary>
    /// True when this interval and <paramref name="other"/> share any time at all. Half-open on both
    /// sides, so an interval ending exactly when another begins does NOT overlap — which is what makes
    /// back-to-back slots bookable instead of mutually exclusive.
    /// </summary>
    public bool Overlaps(DateTime otherStart, DateTime otherEnd) => StartUtc < otherEnd && otherStart < EndUtc;
}

/// <summary>
/// One offerable slot. Carries both the customer-facing range and the range it would occupy, so the
/// booking call does not have to recompute buffers and risk disagreeing with what was offered.
/// </summary>
/// <param name="StartUtc">Appointment start, as told to the customer.</param>
/// <param name="EndUtc">Appointment end, as told to the customer.</param>
/// <param name="BlockStartUtc">Occupied-range start (start minus the before-buffer).</param>
/// <param name="BlockEndUtc">Occupied-range end (end plus the after-buffer).</param>
public readonly record struct AvailableSlot(
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime BlockStartUtc,
    DateTime BlockEndUtc);
