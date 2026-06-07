namespace wa_api.Features.Auth.Dtos;

/// <summary>Result of a successful login or token refresh.</summary>
public record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    CurrentUserResponse User
);
