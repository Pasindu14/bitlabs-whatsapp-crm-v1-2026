namespace wa_api.Features.Auth;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section. SecretKey is required;
/// the rest have safe defaults so dev config only needs the key.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = null!;
    public string Issuer { get; set; } = "wa-api";
    public string Audience { get; set; } = "wa-api";
    // Access tokens are stateless — a deactivation or role change only takes effect when the token expires,
    // and an XSS-leaked token is usable until then. Kept short (60 min) and leaned on refresh-token rotation
    // for longer sessions (H8 / M19). Was 8h.
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 30;
}
