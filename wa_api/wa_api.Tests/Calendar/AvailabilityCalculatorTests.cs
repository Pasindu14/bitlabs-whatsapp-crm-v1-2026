using wa_api.Features.Calendar.Availability;
using wa_api.Features.Calendar.Entities;
using Xunit;

namespace wa_api.Tests.Calendar;

/// <summary>
/// Pins the slot-generation contract. The calculator is the one part of the booking module with no I/O,
/// so every rule about buffers, boundaries, notice periods and daylight saving is settled here rather
/// than against a live calendar.
/// </summary>
public class AvailabilityCalculatorTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static AvailabilityCalculator Calc() => new();

    private static BusinessHours Hours(DayOfWeek day, string start, string end) => new()
    {
        DayOfWeek = day,
        StartLocal = TimeOnly.Parse(start),
        EndLocal = TimeOnly.Parse(end),
    };

    private static SlotRules Rules(
        int duration = 60, int interval = 60, int before = 0, int after = 0, int notice = 0)
        => new(duration, interval, before, after, notice);

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0)
        => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    // 2026-06-01 is a Monday — used as the anchor date throughout.
    private static readonly DateOnly Monday = new(2026, 6, 1);

    // ── Basic generation ──────────────────────────────────────────────────────

    [Fact]
    public void FillsTheWindowWithBackToBackSlots()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Equal(8, slots.Count);

        // Colombo is UTC+05:30 year-round, so 09:00 local is 03:30Z.
        Assert.Equal(Utc(2026, 6, 1, 3, 30), slots[0].StartUtc);
        Assert.Equal(Utc(2026, 6, 1, 4, 30), slots[0].EndUtc);
        Assert.Equal(Utc(2026, 6, 1, 10, 30), slots[^1].StartUtc);
    }

    [Fact]
    public void SlotIntervalFinerThanDurationOffersOverlappingStarts()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "11:00")],
            [], Rules(duration: 60, interval: 30), Utc(2026, 5, 1));

        // 09:00, 09:30, 10:00 — 10:30 is dropped because it would end at 11:30, past close.
        Assert.Equal(3, slots.Count);
        Assert.Equal(Utc(2026, 6, 1, 3, 30), slots[0].StartUtc);
        Assert.Equal(Utc(2026, 6, 1, 4, 0), slots[1].StartUtc);
        Assert.Equal(Utc(2026, 6, 1, 4, 30), slots[2].StartUtc);
    }

    [Fact]
    public void AppointmentThatWouldOverrunClosingIsNotOffered()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "10:30")],
            [], Rules(duration: 60, interval: 60), Utc(2026, 5, 1));

        // Only 09:00 fits. 10:00 would end at 11:00, half an hour past close.
        Assert.Single(slots);
    }

    [Fact]
    public void OnlyMatchingWeekdaysProduceSlots()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Tuesday, "09:00", "17:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Empty(slots);
    }

    [Fact]
    public void TwoWindowsOnOneDayLeaveTheGapUnbookable()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00"), Hours(DayOfWeek.Monday, "13:00", "16:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Equal(6, slots.Count);
        // Nothing starts at noon — that hour is the lunch break.
        Assert.DoesNotContain(slots, s => s.StartUtc == Utc(2026, 6, 1, 6, 30));
    }

    [Fact]
    public void OverlappingWindowsDoNotOfferTheSameStartTwice()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00"), Hours(DayOfWeek.Monday, "10:00", "13:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Equal(slots.Select(s => s.StartUtc).Distinct().Count(), slots.Count);
    }

    // ── Busy intervals ────────────────────────────────────────────────────────

    [Fact]
    public void BusyIntervalRemovesTheSlotItOverlaps()
    {
        // 10:00–11:00 Colombo == 04:30–05:30Z.
        var busy = new BusyInterval(Utc(2026, 6, 1, 4, 30), Utc(2026, 6, 1, 5, 30));

        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00")],
            [busy], Rules(), Utc(2026, 5, 1));

        Assert.Equal(2, slots.Count);
        Assert.DoesNotContain(slots, s => s.StartUtc == Utc(2026, 6, 1, 4, 30));
    }

    [Fact]
    public void BackToBackBookingDoesNotBlockTheAdjacentSlot()
    {
        // Ends exactly when the 10:00 slot begins. Half-open intervals must not treat this as a clash,
        // otherwise every booking would sterilise the slot after it.
        var busy = new BusyInterval(Utc(2026, 6, 1, 3, 30), Utc(2026, 6, 1, 4, 30));

        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00")],
            [busy], Rules(), Utc(2026, 5, 1));

        Assert.Contains(slots, s => s.StartUtc == Utc(2026, 6, 1, 4, 30));
    }

    [Fact]
    public void BlackoutSpanningTheDayRemovesEverything()
    {
        var busy = new BusyInterval(Utc(2026, 5, 31), Utc(2026, 6, 2));

        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [busy], Rules(), Utc(2026, 5, 1));

        Assert.Empty(slots);
    }

    // ── Buffers ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuffersWidenTheBlockedRangeWithoutMovingTheAppointment()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00")],
            [], Rules(before: 15, after: 15), Utc(2026, 5, 1));

        var first = slots[0];
        Assert.Equal(Utc(2026, 6, 1, 3, 30), first.StartUtc);          // told to the customer
        Assert.Equal(Utc(2026, 6, 1, 4, 30), first.EndUtc);
        Assert.Equal(Utc(2026, 6, 1, 3, 15), first.BlockStartUtc);     // actually occupied
        Assert.Equal(Utc(2026, 6, 1, 4, 45), first.BlockEndUtc);
    }

    [Fact]
    public void ABookingsBufferBlocksTheOtherwiseFreeNeighbouringSlot()
    {
        // An existing booking occupying 09:00–10:15 (its own after-buffer included).
        var busy = new BusyInterval(Utc(2026, 6, 1, 3, 30), Utc(2026, 6, 1, 4, 45));

        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "12:00")],
            [busy], Rules(before: 15), Utc(2026, 5, 1));

        // The 10:00 slot's own 15-minute pre-buffer starts at 09:45, inside the existing block.
        Assert.DoesNotContain(slots, s => s.StartUtc == Utc(2026, 6, 1, 4, 30));
        Assert.Contains(slots, s => s.StartUtc == Utc(2026, 6, 1, 5, 30));
    }

    // ── Minimum notice ────────────────────────────────────────────────────────

    [Fact]
    public void SlotsInsideTheNoticePeriodAreNotOffered()
    {
        // 08:00Z on the day itself == 13:30 Colombo. With two hours' notice the earliest bookable
        // start is 10:00Z == 15:30 local, so 15:00 local must be gone and 16:00 must remain.
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [], Rules(notice: 120), Utc(2026, 6, 1, 8, 0));

        Assert.DoesNotContain(slots, s => s.StartUtc == Utc(2026, 6, 1, 9, 30));   // 15:00 local
        Assert.Contains(slots, s => s.StartUtc == Utc(2026, 6, 1, 10, 30));        // 16:00 local
    }

    [Fact]
    public void EntirelyPastDayYieldsNothing()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [], Rules(), Utc(2026, 7, 1));

        Assert.Empty(slots);
    }

    // ── Daylight saving ───────────────────────────────────────────────────────

    [Fact]
    public void SpringForwardGapSkipsTheLocalTimesThatDoNotExist()
    {
        // Europe/London enters BST on 2026-03-29: 01:00 jumps straight to 02:00, so no instant maps to
        // 01:00 local. A calculator that blindly converted would either throw or invent a time.
        var sunday = new DateOnly(2026, 3, 29);

        var slots = Calc().Generate(
            sunday, sunday, London,
            [Hours(DayOfWeek.Sunday, "00:00", "06:00")],
            [], Rules(), Utc(2026, 1, 1));

        Assert.Equal(5, slots.Count);                                  // six cursor positions, one impossible
        Assert.Equal(Utc(2026, 3, 29, 0, 0), slots[0].StartUtc);       // 00:00 GMT
        Assert.Equal(Utc(2026, 3, 29, 1, 0), slots[1].StartUtc);       // 02:00 BST, the gap jumped
    }

    [Fact]
    public void LocalOpeningTimeHoldsAcrossADaylightSavingChange()
    {
        // 09:00 local on both sides of the transition maps to different instants — which is the whole
        // reason business hours are stored as wall-clock rather than UTC.
        var before = new DateOnly(2026, 3, 23);   // Monday, GMT
        var after = new DateOnly(2026, 3, 30);    // Monday, BST

        var hours = new[] { Hours(DayOfWeek.Monday, "09:00", "10:00") };

        var b = Calc().Generate(before, before, London, hours, [], Rules(), Utc(2026, 1, 1));
        var a = Calc().Generate(after, after, London, hours, [], Rules(), Utc(2026, 1, 1));

        Assert.Equal(Utc(2026, 3, 23, 9, 0), b[0].StartUtc);
        Assert.Equal(Utc(2026, 3, 30, 8, 0), a[0].StartUtc);
    }

    // ── Degenerate configuration ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-30, 30)]
    [InlineData(30, 0)]      // a zero interval would never advance the cursor
    [InlineData(30, -15)]
    public void NonPositiveDurationOrIntervalYieldsNothingRatherThanSpinning(int duration, int interval)
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [], Rules(duration: duration, interval: interval), Utc(2026, 5, 1));

        Assert.Empty(slots);
    }

    [Fact]
    public void InvertedDateRangeYieldsNothing()
    {
        var slots = Calc().Generate(
            Monday, Monday.AddDays(-1), Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "17:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Empty(slots);
    }

    [Fact]
    public void WindowEndingBeforeItStartsIsIgnored()
    {
        var slots = Calc().Generate(
            Monday, Monday, Colombo,
            [Hours(DayOfWeek.Monday, "17:00", "09:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Empty(slots);
    }

    [Fact]
    public void NoBusinessHoursMeansNoAvailability()
    {
        var slots = Calc().Generate(Monday, Monday.AddDays(30), Colombo, [], [], Rules(), Utc(2026, 5, 1));
        Assert.Empty(slots);
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public void SlotsComeBackInChronologicalOrderAcrossDays()
    {
        var slots = Calc().Generate(
            Monday, Monday.AddDays(6), Colombo,
            [Hours(DayOfWeek.Monday, "09:00", "11:00"), Hours(DayOfWeek.Wednesday, "09:00", "11:00")],
            [], Rules(), Utc(2026, 5, 1));

        Assert.Equal(4, slots.Count);
        for (var i = 1; i < slots.Count; i++)
            Assert.True(slots[i - 1].StartUtc < slots[i].StartUtc);
    }
}
