using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Calendar.Availability;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Calendar;

/// <inheritdoc />
public class AvailabilityService(
    AppDbContext db,
    ITenantContext tenant,
    IAvailabilityCalculator calculator) : IAvailabilityService
{
    public async Task<AvailabilityResponse> GetAsync(
        Guid appointmentTypeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(appointmentTypeId, fromDate, toDate, ct);

        var slots = calculator.Generate(
            ctx.From, ctx.To, ctx.TimeZone, ctx.Hours, ctx.Busy, ctx.Rules, DateTime.UtcNow);

        return new AvailabilityResponse(
            ctx.TimeZone.Id, ctx.From, ctx.To,
            [.. slots.Select(s => new AvailabilitySlotResponse(s.StartUtc, s.EndUtc))]);
    }

    public async Task<bool> IsOfferableAsync(
        Guid appointmentTypeId, DateTime startUtc, CancellationToken ct = default)
    {
        var start = CompanyTimeZone.AsUtc(startUtc);

        // Resolve the local date of the slot first, then generate only that day. Generating the whole
        // horizon to test one instant would do hundreds of times the work for the same answer.
        var tz = await LoadTimeZoneAsync(ct);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, tz));

        var ctx = await LoadAsync(appointmentTypeId, localDate, localDate, ct);
        var slots = calculator.Generate(
            ctx.From, ctx.To, ctx.TimeZone, ctx.Hours, ctx.Busy, ctx.Rules, DateTime.UtcNow);

        return slots.Any(s => s.StartUtc == start);
    }

    // ── loading ───────────────────────────────────────────────────────────────

    private sealed record Context(
        DateOnly From,
        DateOnly To,
        TimeZoneInfo TimeZone,
        IReadOnlyList<BusinessHours> Hours,
        IReadOnlyList<BusyInterval> Busy,
        SlotRules Rules);

    private async Task<Context> LoadAsync(
        Guid appointmentTypeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var type = await db.AppointmentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == appointmentTypeId, ct)
            ?? throw new NotFoundException("AppointmentType", appointmentTypeId);

        if (!type.IsActive || !type.IsEnabled)
            throw new BusinessRuleException("APPOINTMENT_TYPE_DISABLED",
                "This appointment type is not currently bookable.");

        var tz = await LoadTimeZoneAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var today = CompanyTimeZone.TodayIn(tz, nowUtc);

        // Never offer the past, and never look further ahead than the type commits to. Both bounds are
        // applied here rather than trusted from the query string.
        var from = fromDate is { } f && f > today ? f : today;
        var horizonEnd = today.AddDays(Math.Max(0, type.BookingHorizonDays));
        var to = toDate is { } t && t < horizonEnd ? t : horizonEnd;
        if (to < from) to = from;

        var hours = await db.BusinessHours.AsNoTracking()
            .Where(h => h.IsActive)
            .ToListAsync(ct);

        // Widen the busy query by the buffers so a booking just outside the window whose buffer reaches
        // into it is still seen. Missing those would offer a slot the database would then refuse.
        var pad = TimeSpan.FromMinutes(type.BufferBeforeMinutes + type.BufferAfterMinutes + type.DurationMinutes);
        var windowStart = CompanyTimeZone.DayBoundaryToUtc(from, tz) - pad;
        var windowEnd = CompanyTimeZone.DayBoundaryToUtc(to.AddDays(1), tz) + pad;

        var busy = new List<BusyInterval>();

        // Appointments occupy their BLOCK range (buffers included), not the customer-facing range.
        // Cancelled is the only status that frees a slot — matching the database's overlap constraint.
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.Status != AppointmentStatus.Cancelled
                     && a.BlockStartUtc < windowEnd && windowStart < a.BlockEndUtc)
            .Select(a => new { a.BlockStartUtc, a.BlockEndUtc })
            .ToListAsync(ct);
        busy.AddRange(booked.Select(b => new BusyInterval(b.BlockStartUtc, b.BlockEndUtc)));

        var blackouts = await db.CalendarBlackouts.AsNoTracking()
            .Where(b => b.IsActive && b.StartUtc < windowEnd && windowStart < b.EndUtc)
            .Select(b => new { b.StartUtc, b.EndUtc })
            .ToListAsync(ct);
        busy.AddRange(blackouts.Select(b => new BusyInterval(b.StartUtc, b.EndUtc)));

        var rules = new SlotRules(
            type.DurationMinutes, type.SlotIntervalMinutes,
            type.BufferBeforeMinutes, type.BufferAfterMinutes, type.MinimumNoticeMinutes);

        return new Context(from, to, tz, hours, busy, rules);
    }

    private async Task<TimeZoneInfo> LoadTimeZoneAsync(CancellationToken ct)
    {
        var companyId = tenant.CompanyId
            ?? throw new BusinessRuleException("TENANT_REQUIRED",
                "This endpoint must be called by a company user.");

        // Company carries no global query filter (it IS the tenant), so scoping is explicit.
        var timeZoneId = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        return CompanyTimeZone.ResolveOrFallback(timeZoneId);
    }
}
