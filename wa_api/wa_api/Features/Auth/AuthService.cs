using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Auth.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Auth;

public class AuthService(AppDbContext db, IJwtTokenService tokenService) : IAuthService
{
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
        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokenService.CreateAccessToken(user);
        return new AuthResponse(accessToken, expiresAt, Map(user));
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new InvalidTokenException();

        return Map(user);
    }

    private static CurrentUserResponse Map(User u)
        => new(u.Id, u.Email, u.FullName, u.Role, u.Permissions, u.CompanyId);
}
