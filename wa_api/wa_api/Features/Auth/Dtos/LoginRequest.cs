using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Auth.Dtos;

/// <summary>Credentials for <c>POST /api/v1/auth/login</c>.</summary>
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);
