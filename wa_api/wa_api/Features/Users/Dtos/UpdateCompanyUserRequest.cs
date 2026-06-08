using System.ComponentModel.DataAnnotations;
using wa_api.Features.Auth;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Payload for <c>PUT /api/v1/team-users/{id}</c> (CompanyAdmin only). No CompanyId —
/// the user always stays in the caller's own company; a CompanyAdmin cannot move a user
/// to another company. The password is NOT editable here — use the reset-password flow.
/// </summary>
public record UpdateCompanyUserRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required] UserRole Role,
    // Capability grants from the Permission catalog. Honoured only for Agents.
    IReadOnlyList<string>? Permissions
);
