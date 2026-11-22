using wa_api.Features.ClinicCrm.Dtos;

namespace wa_api.Features.ClinicCrm;

/// <summary>The company admin's side of the clinic CRM connection: credentials, a test, treatment mapping.</summary>
public interface IClinicCrmSettingsService
{
    Task<ClinicCrmSettingsResponse> GetAsync(CancellationToken ct = default);

    /// <summary>Creates or updates the company's connection. A new connection must include the secret.</summary>
    Task<ClinicCrmSettingsResponse> SaveConnectionAsync(SaveClinicCrmConnectionRequest request, CancellationToken ct = default);

    /// <summary>Removes the connection; the bot goes back to our own calendar.</summary>
    Task DeleteConnectionAsync(CancellationToken ct = default);

    /// <summary>Gets a token and the treatment list. Creates nothing in the CRM.</summary>
    Task<ClinicCrmTestResponse> TestAsync(CancellationToken ct = default);

    /// <summary>The CRM's current treatment list, for the mapping dropdowns.</summary>
    Task<IReadOnlyList<ClinicCrmTreatmentResponse>> GetTreatmentsAsync(CancellationToken ct = default);

    /// <summary>Sets which clinic treatment each listed service books as.</summary>
    Task<ClinicCrmSettingsResponse> SaveMappingsAsync(SaveClinicCrmMappingsRequest request, CancellationToken ct = default);
}
