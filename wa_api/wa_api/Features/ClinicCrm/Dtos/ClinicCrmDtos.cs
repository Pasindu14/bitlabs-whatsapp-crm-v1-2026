using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.ClinicCrm.Dtos;

/// <summary>
/// The settings screen in one payload. The client secret is never returned — only whether one is saved.
/// </summary>
public record ClinicCrmSettingsResponse(
    bool Configured,
    string? BaseUrl,
    string? ClientId,
    bool HasSecret,
    bool IsEnabled,
    DateTime? LastSuccessAt,
    string? LastError,
    DateTime? LastErrorAt,
    IReadOnlyList<ClinicCrmServiceMappingResponse> Services
);

/// <summary>One of our bookable services and the clinic treatment it books as (null = books with none).</summary>
public record ClinicCrmServiceMappingResponse(
    Guid AppointmentTypeId,
    string Name,
    bool IsEnabled,
    int? ExternalTreatmentId
);

/// <summary>
/// Body for PUT /clinic-crm. Leave <see cref="ClientSecret"/> empty to keep the saved one — the screen
/// never has it to send back.
/// </summary>
public record SaveClinicCrmConnectionRequest(
    [Required][MaxLength(500)] string BaseUrl,
    [Required][MaxLength(200)] string ClientId,
    [MaxLength(2048)] string? ClientSecret,
    bool IsEnabled = true
);

/// <summary>A treatment from the clinic's CRM.</summary>
public record ClinicCrmTreatmentResponse(int Id, string Name, string? Description);

/// <summary>Result of "Test connection": a token and the treatment list, nothing written to the CRM.</summary>
public record ClinicCrmTestResponse(bool Ok, string Message, IReadOnlyList<ClinicCrmTreatmentResponse> Treatments);

/// <summary>One row of PUT /clinic-crm/mappings.</summary>
public record ClinicCrmMappingItem(
    Guid AppointmentTypeId,
    [Range(1, int.MaxValue)] int? ExternalTreatmentId
);

/// <summary>Body for PUT /clinic-crm/mappings. Services not listed are left as they are.</summary>
public record SaveClinicCrmMappingsRequest(
    [Required] List<ClinicCrmMappingItem> Items
);
