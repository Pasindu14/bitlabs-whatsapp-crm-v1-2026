using Microsoft.EntityFrameworkCore;
using wa_api.Features.Auth;

namespace wa_api.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds baseline data on startup (Development/Staging).
/// Seed payloads are added per phase — the platform super-admin lands in Phase 2.5.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(cancellationToken);
        }

        logger.LogInformation("Database ready ({Count} migration(s) applied this run).", pending.Count);

        await SeedSuperAdminAsync(db, scope.ServiceProvider, logger, cancellationToken);
    }

    /// <summary>
    /// Ensures exactly one platform SuperAdmin exists. Credentials come from the
    /// <c>Seed:SuperAdmin</c> config section, falling back to dev defaults (logged loudly).
    /// </summary>
    private static async Task SeedSuperAdminAsync(AppDbContext db, IServiceProvider services,
        ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, ct))
            return;

        var config = services.GetRequiredService<IConfiguration>();
        var email = (config["Seed:SuperAdmin:Email"] ?? "superadmin@btilabs.com").Trim().ToLowerInvariant();
        var password = config["Seed:SuperAdmin:Password"] ?? "ChangeMe123!";
        var fullName = config["Seed:SuperAdmin:FullName"] ?? "Platform Super Admin";

        db.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            Role = UserRole.SuperAdmin,
            CompanyId = null
        });
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded platform SuperAdmin '{Email}'. CHANGE THE PASSWORD if using the default.", email);
    }
}
