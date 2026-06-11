using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Companies.Dtos;

/// <summary>Payload for <c>POST /api/v1/companies/provision</c> (SuperAdmin only).
/// Atomically creates a company and its first CompanyAdmin in one transaction.</summary>
public record ProvisionCompanyRequest(
    // — Company fields —
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [StringLength(120)] string? Slug,
    [EmailAddress, StringLength(256)] string? CompanyEmail,
    [StringLength(32)] string? Phone,
    // — Admin user fields —
    [Required, StringLength(200, MinimumLength = 2)] string AdminFullName,
    [Required, EmailAddress, StringLength(256)] string AdminEmail,
    [Required, StringLength(128, MinimumLength = 8)] string AdminPassword
);
