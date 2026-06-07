namespace wa_api.Features.Auth;

/// <summary>Issues signed JWT access tokens for authenticated users.</summary>
public interface IJwtTokenService
{
    /// <summary>Builds a signed access token carrying the user's id, role and companyId.</summary>
    (string AccessToken, DateTime ExpiresAt) CreateAccessToken(User user);
}
