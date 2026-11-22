using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Calendar;

/// <inheritdoc />
public class AppointmentTypeService(AppDbContext db) : IAppointmentTypeService
{
    public async Task<IReadOnlyList<AppointmentTypeResponse>> GetAllAsync(
        bool enabledOnly = false, CancellationToken ct = default)
    {
        // The global query filter already scopes this to the caller's company.
        var query = db.AppointmentTypes.AsNoTracking().Where(t => t.IsActive);

        if (enabledOnly)
            query = query.Where(t => t.IsEnabled);

        return await query
            .OrderBy(t => t.Name)
            .Select(t => Project(t))
            .ToListAsync(ct);
    }

    public async Task<AppointmentTypeResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var type = await db.AppointmentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("AppointmentType", id);
        return Map(type);
    }

    public async Task<AppointmentTypeResponse> CreateAsync(
        CreateAppointmentTypeRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        // Scoped by the query filter, so uniqueness is per-company rather than global.
        if (await db.AppointmentTypes.AnyAsync(t => t.Name == name && t.IsActive, ct))
            throw new ConflictException("APPOINTMENT_TYPE_DUPLICATE",
                "An appointment type with this name already exists.");

        ValidateShape(request.DurationMinutes, request.SlotIntervalMinutes);

        var type = new AppointmentType
        {
            Name = name,
            Description = request.Description?.Trim(),
            DurationMinutes = request.DurationMinutes,
            SlotIntervalMinutes = request.SlotIntervalMinutes,
            BufferBeforeMinutes = request.BufferBeforeMinutes,
            BufferAfterMinutes = request.BufferAfterMinutes,
            MinimumNoticeMinutes = request.MinimumNoticeMinutes,
            BookingHorizonDays = request.BookingHorizonDays,
            Price = request.Price,
            // CompanyId is auto-stamped from tenant context by AuditInterceptor on insert.
        };
        db.AppointmentTypes.Add(type);
        await db.SaveChangesAsync(ct);

        return Map(type);
    }

    public async Task<AppointmentTypeResponse> UpdateAsync(
        Guid id, UpdateAppointmentTypeRequest request, CancellationToken ct = default)
    {
        var type = await db.AppointmentTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("AppointmentType", id);

        var name = request.Name.Trim();
        if (await db.AppointmentTypes.AnyAsync(t => t.Name == name && t.Id != id && t.IsActive, ct))
            throw new ConflictException("APPOINTMENT_TYPE_DUPLICATE",
                "An appointment type with this name already exists.");

        ValidateShape(request.DurationMinutes, request.SlotIntervalMinutes);

        type.Name = name;
        type.Description = request.Description?.Trim();
        type.DurationMinutes = request.DurationMinutes;
        type.SlotIntervalMinutes = request.SlotIntervalMinutes;
        type.BufferBeforeMinutes = request.BufferBeforeMinutes;
        type.BufferAfterMinutes = request.BufferAfterMinutes;
        type.MinimumNoticeMinutes = request.MinimumNoticeMinutes;
        type.BookingHorizonDays = request.BookingHorizonDays;
        type.Price = request.Price;
        type.IsEnabled = request.IsEnabled;

        // Existing appointments are deliberately NOT reshaped. They were agreed with a customer at a
        // specific time and length; changing the type governs future bookings only.
        await db.SaveChangesAsync(ct);

        return Map(type);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var type = await db.AppointmentTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("AppointmentType", id);

        if (!type.IsActive) return;   // idempotent

        type.IsActive = false;
        type.IsEnabled = false;
        await db.SaveChangesAsync(ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Catches the one combination the [Range] attributes cannot: an interval coarser than the duration
    /// is fine (sparser starts), but an interval finer than the duration deliberately offers overlapping
    /// starts, which is only sane because the overlap constraint rejects the second booking. Both are
    /// allowed; what is not is a slot longer than the shortest opening window would ever fit, which the
    /// calculator handles by emitting nothing — so this only guards genuinely nonsensical input.
    /// </summary>
    private static void ValidateShape(int durationMinutes, int slotIntervalMinutes)
    {
        if (durationMinutes <= 0 || slotIntervalMinutes <= 0)
            throw new BusinessRuleException("APPOINTMENT_TYPE_INVALID",
                "Duration and slot interval must both be greater than zero.");
    }

    private static AppointmentTypeResponse Project(AppointmentType t) => new(
        t.Id, t.Name, t.Description, t.DurationMinutes, t.SlotIntervalMinutes,
        t.BufferBeforeMinutes, t.BufferAfterMinutes, t.MinimumNoticeMinutes,
        t.BookingHorizonDays, t.Price, t.IsEnabled, t.IsActive, t.CreatedAt);

    private static AppointmentTypeResponse Map(AppointmentType t) => Project(t);
}
