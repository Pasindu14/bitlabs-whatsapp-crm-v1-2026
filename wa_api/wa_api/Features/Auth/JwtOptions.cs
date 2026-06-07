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
    public int AccessTokenExpiryMinutes { get; set; } = 480; // 8 hours
}
