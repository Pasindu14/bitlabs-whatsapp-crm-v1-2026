using wa_api.Features.Calendar.Dtos;

namespace wa_api.Features.Calendar;

/// <summary>CRUD over the bookable services a company offers.</summary>
public interface IAppointmentTypeService
{
    /// <summary>All types for the caller's company, newest first. <paramref name="enabledOnly"/> filters
    /// to what a customer could actually book right now.</summary>
    Task<IReadOnlyList<AppointmentTypeResponse>> GetAllAsync(
        bool enabledOnly = false, CancellationToken ct = default);

    /// <summary>Single type; 404 when it isn't visible to the caller.</summary>
    Task<AppointmentTypeResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AppointmentTypeResponse> CreateAsync(CreateAppointmentTypeRequest request, CancellationToken ct = default);

    Task<AppointmentTypeResponse> UpdateAsync(Guid id, UpdateAppointmentTypeRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes a type. Existing appointments keep referencing it, so it is never removed.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
