using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Companies.Dtos;

/// <summary>Payload for <c>POST /api/v1/companies</c> (SuperAdmin only).</summary>
public record CreateCompanyRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [StringLength(120)] string? Slug,
    [EmailAddress, StringLength(256)] string? Email,
    // No format validation — phone is free-form (international formats vary widely).
    [StringLength(32)] string? Phone
);
