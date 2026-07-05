using Microsoft.EntityFrameworkCore;
using wa_api.Features.Auth;
using wa_api.Features.Plans.Entities;

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
        await SeedPlansAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Seeds the baseline plan catalog (Free / Starter / Pro) once. Idempotent: skips entirely
    /// if any plan already exists, so edited quotas/prices are never overwritten on restart.
    /// </summary>
    private static async Task SeedPlansAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Plans.AnyAsync(ct))
            return;

        db.Plans.AddRange(
            new Plan
            {
                Name = "Free",
                MonthlyMessageQuota = 1_000,
                Price = 0m,
                Currency = "USD",
                FeatureFlags = [Permission.LiveMessage, Permission.ContactList],
            },
            new Plan
            {
                Name = "Starter",
                MonthlyMessageQuota = 10_000,
                Price = 29m,
                Currency = "USD",
                FeatureFlags =
                [
                    Permission.LiveMessage, Permission.ContactList,
                    Permission.ManageTemplate, Permission.ScheduleCampaign,
                ],
            },
            new Plan
            {
                Name = "Pro",
                MonthlyMessageQuota = 100_000,
                Price = 99m,
                Currency = "USD",
                FeatureFlags =
                [
                    Permission.LiveMessage, Permission.ContactList,
                    Permission.ManageTemplate, Permission.ScheduleCampaign,
                    Permission.TokenPurchase, Permission.Analytics,
                ],
            });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded baseline plan catalog: Free, Starter, Pro.");
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
        var fullName = config["Seed:SuperAdmin:FullName"] ?? "Platform Super Admin";

        // Require an explicit seed password outside Development (L5): a Staging box is internet-reachable and,
        // combined with a known default credential, is a takeover risk. Refuse to seed the built-in default
        // there; in Development, fall back but warn loudly.
        var configuredPassword = config["Seed:SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            var hostEnv = services.GetRequiredService<IHostEnvironment>();
            if (!hostEnv.IsDevelopment())
                throw new InvalidOperationException(
                    "Seed:SuperAdmin:Password is not configured. Refusing to seed a SuperAdmin with the " +
                    "built-in default outside Development — set an explicit seed password.");

            logger.LogWarning(
                "Seed:SuperAdmin:Password not configured — seeding with the INSECURE dev default. " +
                "Set Seed:SuperAdmin:Password before exposing this environment.");
        }
        var password = configuredPassword ?? "ChangeMe123!";

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
