using wa_api.Features.Auth.Dtos;

namespace wa_api.Features.Auth;

public interface IAuthService
{
    /// <summary>Verifies credentials and returns an access token + refresh token, or throws on failure.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Exchanges a valid refresh token for a new access token + rotated refresh token.</summary>
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes a refresh token on logout (best-effort; no error if token not found).</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);

    /// <summary>Loads the current user by id (from the token's "sub" claim).</summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
