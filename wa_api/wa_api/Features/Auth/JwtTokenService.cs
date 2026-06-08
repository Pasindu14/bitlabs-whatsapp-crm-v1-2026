using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace wa_api.Features.Auth;

/// <summary>
/// HS256 token issuer. Emits RAW claim names ("sub", "role", "email", "companyId")
/// to match the bearer setup in Program.cs (MapInboundClaims = false), so
/// <c>[Authorize(Roles = "SuperAdmin")]</c> reads the "role" claim directly.
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string AccessToken, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("name", user.FullName),
            new("role", user.Role.ToString())
        };

        if (user.CompanyId is { } companyId)
            claims.Add(new Claim("companyId", companyId.ToString()));

        // Fine-grained capability grants (Agents only) as a JSON array claim, for UI gating.
        if (user.Permissions.Count > 0)
            claims.Add(new Claim("permissions",
                JsonSerializer.Serialize(user.Permissions),
                JsonClaimValueTypes.JsonArray));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
