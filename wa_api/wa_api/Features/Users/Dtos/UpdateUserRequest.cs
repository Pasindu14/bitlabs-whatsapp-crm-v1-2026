using System.ComponentModel.DataAnnotations;
using wa_api.Features.Auth;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Payload for <c>PUT /api/v1/users/{id}</c> (SuperAdmin only).
/// The password is NOT editable here — use <c>POST /api/v1/users/{id}/reset-password</c>.
/// </summary>
public record UpdateUserRequest(
    [Required] Guid CompanyId,
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required] UserRole Role
);
