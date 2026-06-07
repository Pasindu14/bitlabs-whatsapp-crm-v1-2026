using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/users/{id}/reset-password</c> (SuperAdmin only).
/// Replaces the user's password outright with a freshly hashed one.
/// </summary>
public record ResetPasswordRequest(
    [Required, StringLength(128, MinimumLength = 8)] string Password
);
