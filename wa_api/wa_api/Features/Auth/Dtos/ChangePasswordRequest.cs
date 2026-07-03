using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Auth.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/auth/change-password</c> — a self-service password change for the
/// currently authenticated user (any role). The caller must prove they know the current password;
/// the account id is taken from the bearer token, never the body.
/// </summary>
public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 8)] string NewPassword
);
