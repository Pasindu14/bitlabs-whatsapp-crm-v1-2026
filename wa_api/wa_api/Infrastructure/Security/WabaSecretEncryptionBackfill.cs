using Microsoft.EntityFrameworkCore;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Infrastructure.Security;

/// <summary>
/// One-time-ish startup backfill (H3): encrypts any <c>WabaConnection</c> secrets still stored as plaintext.
/// The value converter already encrypts every NEW write and reads legacy plaintext transparently, so this is
/// what brings EXISTING rows to rest-encrypted. Idempotent — rows whose stored value already carries the
/// <c>enc:v1:</c> prefix are skipped. No-op under the passthrough protector (no key configured), and any
/// failure is swallowed so it can never block startup.
/// </summary>
public static class WabaSecretEncryptionBackfill
{
    public static async Task RunAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();
        if (protector is NullTokenProtector)
            return;   // no encryption key configured — nothing to do

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Database.IsRelational())
            return;   // raw prefix probe needs SQL

        try
        {
            // Read the STORED values via raw SQL — a normal EF read would apply the converter and hide the
            // prefix. Rows missing the prefix on either secret still need encrypting.
            var ids = await db.Database.SqlQueryRaw<Guid>(
                    "SELECT \"Id\" AS \"Value\" FROM \"WabaConnections\" " +
                    "WHERE \"EncryptedAccessToken\" NOT LIKE 'enc:v1:%' OR \"AppSecret\" NOT LIKE 'enc:v1:%'")
                .ToListAsync();

            if (ids.Count == 0)
                return;

            var conns = await db.WabaConnections.IgnoreQueryFilters()
                .Where(c => ids.Contains(c.Id))
                .ToListAsync();

            // The entities loaded with plaintext in memory (converter decrypted/passed through). Force both
            // secret columns modified so SaveChanges re-writes them through the converter's Protect().
            foreach (var c in conns)
            {
                db.Entry(c).Property(x => x.EncryptedAccessToken).IsModified = true;
                db.Entry(c).Property(x => x.AppSecret).IsModified = true;
            }

            await db.SaveChangesAsync();
            logger.LogInformation("WABA secret backfill: encrypted {Count} connection(s) at rest.", conns.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WABA secret backfill failed — connections remain as-is; will retry next startup.");
        }
    }
}
