namespace wa_api.Features.Auth.Dtos;

/// <summary>Body for <c>POST /api/v1/auth/logout</c> — refresh token revocation is optional.</summary>
public record LogoutRequest(string? RefreshToken = null);
