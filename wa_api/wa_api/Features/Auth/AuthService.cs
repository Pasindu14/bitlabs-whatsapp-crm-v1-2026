using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using wa_api.Common.Errors;
using wa_api.Features.Auth.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Auth;

public class AuthService(AppDbContext db, IJwtTokenService tokenService, IOptions<JwtOptions> options) : IAuthService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);

        // Same error whether the email is unknown or the password is wrong —
        // never reveal which accounts exist.
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AuthenticationException("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");

        user.LastLoginAt = DateTime.UtcNow;

        var refreshToken = CreateRefreshTokenEntity(user.Id);
        db.RefreshTokens.Add(refreshToken);

        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokenService.CreateAccessToken(user);
        return new AuthResponse(accessToken, expiresAt, refreshToken.Token, refreshToken.ExpiresAt, Map(user));
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(
                t => t.Token == refreshToken && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow,
                ct)
            ?? throw new AuthenticationException("AUTH_INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");

        if (!existing.User.IsActive)
            throw new AuthenticationException("AUTH_INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");

        // Revoke old token before issuing the next one (rotation — prevents replay).
        existing.IsRevoked = true;

        var newRefreshToken = CreateRefreshTokenEntity(existing.UserId);
        db.RefreshTokens.Add(newRefreshToken);

        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokenService.CreateAccessToken(existing.User);
        return new AuthResponse(accessToken, expiresAt, newRefreshToken.Token, newRefreshToken.ExpiresAt, Map(existing.User));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken && !t.IsRevoked, ct);

        if (token is not null)
        {
            token.IsRevoked = true;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new InvalidTokenException();

        return Map(user);
    }

    private RefreshToken CreateRefreshTokenEntity(Guid userId) => new()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        UserId = userId,
        ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpiryDays)
    };

    private static CurrentUserResponse Map(User u)
        => new(u.Id, u.Email, u.FullName, u.Role, u.Permissions, u.CompanyId);
}
