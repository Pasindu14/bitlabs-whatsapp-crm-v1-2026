namespace wa_api.Features.Auth.Dtos;

/// <summary>Result of a successful login: the bearer token plus the signed-in user.</summary>
public record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    CurrentUserResponse User
);
