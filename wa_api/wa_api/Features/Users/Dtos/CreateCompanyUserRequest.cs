using System.ComponentModel.DataAnnotations;
using wa_api.Features.Auth;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/team-users</c> (CompanyAdmin only). Creates a user
/// (CompanyAdmin/Agent) for the CALLER'S OWN company. There is intentionally no
/// CompanyId here — the owning company is taken server-side from the JWT, never the
/// request body, so a CompanyAdmin can never create a user for another company.
/// SuperAdmin accounts are NOT creatable here.
/// </summary>
public record CreateCompanyUserRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    // Defaults to Agent server-side when null. Must be a tenant role (CompanyAdmin/Agent).
    UserRole? Role,
    // Capability grants from the Permission catalog. Honoured only for Agents — a
    // CompanyAdmin is all-access by role, so any value here is ignored for that role.
    IReadOnlyList<string>? Permissions
);
