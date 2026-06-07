using wa_api.Features.Auth.Dtos;

namespace wa_api.Features.Auth;

public interface IAuthService
{
    /// <summary>Verifies credentials and returns a token + user, or throws on failure.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Loads the current user by id (from the token's "sub" claim).</summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
