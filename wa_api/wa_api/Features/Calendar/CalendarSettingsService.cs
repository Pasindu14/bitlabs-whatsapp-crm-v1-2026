using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Calendar;

/// <inheritdoc />
public class CalendarSettingsService(AppDbContext db, ITenantContext tenant) : ICalendarSettingsService
{
    public async Task<CalendarSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var company = await LoadCompanyAsync(ct);
        var hours = await LoadHoursAsync(ct);
        return new CalendarSettingsResponse(company.TimeZoneId, hours);
    }

    public async Task<CalendarSettingsResponse> UpdateTimeZoneAsync(
        UpdateTimeZoneRequest request, CancellationToken ct = default)
    {
        var id = request.TimeZoneId.Trim();

        // Validated here rather than at read time: an unresolvable id stored now would silently degrade
        // every future availability query to UTC, which looks like a booking bug rather than a config one.
        if (!CompanyTimeZone.IsValid(id))
            throw new BusinessRuleException("TIMEZONE_INVALID",
                $"'{id}' is not a timezone this server recognises. Use an IANA id such as 'Asia/Colombo'.");

        var company = await LoadCompanyAsync(ct);
        company.TimeZoneId = id;
        await db.SaveChangesAsync(ct);

        return new CalendarSettingsResponse(company.TimeZoneId, await LoadHoursAsync(ct));
    }

    public async Task<CalendarSettingsResponse> SaveBusinessHoursAsync(
        SaveBusinessHoursRequest request, CancellationToken ct = default)
    {
        foreach (var w in request.Windows)
        {
            if (w.EndLocal <= w.StartLocal)
                throw new BusinessRuleException("BUSINESS_HOURS_INVALID",
                    $"{w.DayOfWeek}: closing time must be after opening time. A window that crosses "
                    + "midnight is entered as two windows on two days.");
        }

        var companyId = RequireCompanyId();
        var existing = await db.BusinessHours.ToListAsync(ct);

        // Physically removed, unlike most entities here. The platform's soft-delete convention exists to
        // preserve rows that history still references; a superseded opening window references nothing and
        // means nothing once replaced, so keeping it would grow the table on every settings save and slow
        // the read that the availability calculator makes on every query.
        db.BusinessHours.RemoveRange(existing);

        foreach (var w in request.Windows)
        {
            db.BusinessHours.Add(new BusinessHours
            {
                CompanyId = companyId,   // explicit: the interceptor stamps inserts, and this is clearer at the call site
                DayOfWeek = w.DayOfWeek,
                StartLocal = w.StartLocal,
                EndLocal = w.EndLocal,
            });
        }

        await db.SaveChangesAsync(ct);

        var company = await LoadCompanyAsync(ct);
        return new CalendarSettingsResponse(company.TimeZoneId, await LoadHoursAsync(ct));
    }

    public async Task<IReadOnlyList<BlackoutResponse>> GetBlackoutsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var from = CompanyTimeZone.AsUtc(fromUtc);
        var to = CompanyTimeZone.AsUtc(toUtc);

        // Half-open overlap: a blackout touching the window at either edge counts as inside it.
        return await db.CalendarBlackouts.AsNoTracking()
            .Where(b => b.IsActive && b.StartUtc < to && from < b.EndUtc)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BlackoutResponse(b.Id, b.StartUtc, b.EndUtc, b.Reason))
            .ToListAsync(ct);
    }

    public async Task<BlackoutResponse> CreateBlackoutAsync(
        CreateBlackoutRequest request, CancellationToken ct = default)
    {
        var start = CompanyTimeZone.AsUtc(request.StartUtc);
        var end = CompanyTimeZone.AsUtc(request.EndUtc);

        if (end <= start)
            throw new BusinessRuleException("BLACKOUT_INVALID_RANGE", "The end must be after the start.");

        var blackout = new CalendarBlackout
        {
            StartUtc = start,
            EndUtc = end,
            Reason = request.Reason.Trim(),
            // CompanyId is auto-stamped from tenant context by AuditInterceptor on insert.
        };
        db.CalendarBlackouts.Add(blackout);
        await db.SaveChangesAsync(ct);

        return new BlackoutResponse(blackout.Id, blackout.StartUtc, blackout.EndUtc, blackout.Reason);
    }

    public async Task DeleteBlackoutAsync(Guid id, CancellationToken ct = default)
    {
        // The query filter scopes this to the caller's company, so a foreign id is a clean 404.
        var blackout = await db.CalendarBlackouts.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("Blackout", id);

        if (!blackout.IsActive) return;   // idempotent

        // Soft-deleted, unlike business hours: "we were closed that week" stays answerable afterwards.
        blackout.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid RequireCompanyId() =>
        tenant.CompanyId ?? throw new BusinessRuleException("TENANT_REQUIRED",
            "This endpoint must be called by a company user.");

    private async Task<Companies.Company> LoadCompanyAsync(CancellationToken ct)
    {
        var companyId = RequireCompanyId();

        // Company carries no global query filter (it IS the tenant), so scoping is explicit here.
        return await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);
    }

    private async Task<IReadOnlyList<BusinessHoursResponse>> LoadHoursAsync(CancellationToken ct) =>
        await db.BusinessHours.AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.DayOfWeek).ThenBy(h => h.StartLocal)
            .Select(h => new BusinessHoursResponse(h.Id, h.DayOfWeek, h.StartLocal, h.EndLocal))
            .ToListAsync(ct);
}
