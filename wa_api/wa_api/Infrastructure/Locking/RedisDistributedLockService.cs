using StackExchange.Redis;

namespace wa_api.Infrastructure.Locking;

/// <summary>
/// Distributed lock via Redis (<c>SET key value NX PX ttl</c>), released safely with a
/// compare-and-delete Lua script so a process only ever releases its own lock.
/// Used when <c>REDIS_CONNECTION</c> is configured. Works across all worker replicas.
/// </summary>
public class RedisDistributedLockService(IConnectionMultiplexer redis,
    ILogger<RedisDistributedLockService> logger) : IDistributedLockService
{
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly IDatabase _db = redis.GetDatabase();
    private readonly ILogger<RedisDistributedLockService> _logger = logger;

    public async Task<IAsyncDisposable?> AcquireAsync(string resource, CancellationToken ct = default)
    {
        var key = $"lock:{resource}";
        var token = Guid.NewGuid().ToString("N");

        var acquired = await _db.StringSetAsync(key, token, _ttl, When.NotExists);
        if (!acquired)
        {
            _logger.LogWarning("Failed to acquire Redis lock for resource {Resource}", resource);
            return null;
        }

        _logger.LogDebug("Acquired Redis lock for {Resource}", resource);
        return new RedisLockHandle(_db, key, token, resource, _logger);
    }

    private sealed class RedisLockHandle(IDatabase db, string key, string token,
        string resource, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
                logger.LogDebug("Released Redis lock for {Resource}", resource);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release Redis lock for {Resource}", resource);
            }
        }
    }
}
