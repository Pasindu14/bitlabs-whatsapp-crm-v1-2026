using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Companies.Dtos;

/// <summary>Payload for <c>PUT /api/v1/companies/{id}</c> (SuperAdmin only). Full replace of editable fields.</summary>
public record UpdateCompanyRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [StringLength(120)] string? Slug,
    [EmailAddress, StringLength(256)] string? Email,
    // No format validation — phone is free-form (international formats vary widely).
    [StringLength(32)] string? Phone
);
