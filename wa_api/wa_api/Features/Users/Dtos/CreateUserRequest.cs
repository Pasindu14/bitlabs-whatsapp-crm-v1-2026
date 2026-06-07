using System.ComponentModel.DataAnnotations;
using wa_api.Features.Auth;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/users</c> (SuperAdmin only). Creates a tenant user
/// (CompanyAdmin/Agent) for the selected company. SuperAdmin accounts are NOT creatable here.
/// </summary>
public record CreateUserRequest(
    [Required] Guid CompanyId,
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    // Defaults to CompanyAdmin server-side when null. Must be a tenant role.
    UserRole? Role
);
