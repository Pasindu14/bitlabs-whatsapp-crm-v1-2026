using Npgsql;

namespace wa_api.Infrastructure.Locking;

/// <summary>
/// Distributed lock via Postgres session-level advisory locks (<c>pg_try_advisory_lock</c>).
/// Holds a dedicated connection for the lock's lifetime — requires a SESSION-mode connection
/// (the Supavisor session pooler on port 5432 keeps connections sticky, so this is safe).
/// Used when no Redis is configured.
/// </summary>
public class PostgresAdvisoryLockService(IConfiguration config,
    ILogger<PostgresAdvisoryLockService> logger) : IDistributedLockService
{
    private readonly string _connectionString = config.GetConnectionString("DefaultConnection")!;
    private readonly ILogger<PostgresAdvisoryLockService> _logger = logger;

    public async Task<IAsyncDisposable?> AcquireAsync(string resource, CancellationToken ct = default)
    {
        var lockKey = StableHash(resource);
        var conn = new NpgsqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            cmd.Parameters.AddWithValue("key", lockKey);

            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct))!;
            if (!acquired)
            {
                _logger.LogWarning("Failed to acquire advisory lock for resource {Resource}", resource);
                await conn.DisposeAsync();
                return null;
            }

            _logger.LogDebug("Acquired advisory lock for {Resource}", resource);
            return new AdvisoryLockHandle(conn, lockKey, resource, _logger);
        }
        catch
        {
            // Cancellation (or any failure) after OpenAsync would otherwise abandon an open session
            // and leak it from the pool — dispose the connection on every non-success exit. The
            // handle owns the connection only on the success path above.
            await conn.DisposeAsync();
            throw;
        }
    }

    // Deterministic 64-bit key (string.GetHashCode is randomized per-process).
    private static long StableHash(string s)
    {
        unchecked
        {
            ulong hash = 14695981039346656037; // FNV-1a 64-bit offset basis
            foreach (var c in s) { hash ^= c; hash *= 1099511628211; }
            return (long)hash;
        }
    }

    private sealed class AdvisoryLockHandle(NpgsqlConnection conn, long lockKey,
        string resource, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                cmd.Parameters.AddWithValue("key", lockKey);
                await cmd.ExecuteScalarAsync();
                logger.LogDebug("Released advisory lock for {Resource}", resource);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release advisory lock for {Resource}", resource);
            }
            finally
            {
                await conn.DisposeAsync();
            }
        }
    }
}
