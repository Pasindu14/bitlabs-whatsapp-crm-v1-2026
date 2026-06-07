using Microsoft.EntityFrameworkCore;

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

        // ── Seed data is added per phase (Phase 2.5: platform super-admin). ──
    }
}
