using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace wa_api.Infrastructure.Caching;

/// <summary>
/// <see cref="ICacheService"/> over <see cref="IDistributedCache"/> — backed by Redis
/// when <c>REDIS_CONNECTION</c> is set, otherwise an in-process distributed memory cache.
/// Cache failures degrade gracefully (logged, never thrown).
/// </summary>
public class DistributedCacheService(
    IDistributedCache cache,
    IServiceProvider services,
    ILogger<DistributedCacheService> logger) : ICacheService
{
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<DistributedCacheService> _logger = logger;

    // Present only in Redis mode (registered as a singleton in Program.cs). Resolved optionally so the
    // in-memory cache path keeps working without it. Gives IncrementAsync a true atomic INCR.
    private readonly IConnectionMultiplexer? _redis = services.GetService<IConnectionMultiplexer>();

    // Shared across scoped instances so RemoveByPrefix can enumerate keys this process set.
    private static readonly ConcurrentDictionary<string, bool> _trackedKeys = new();

    // Serializes the in-memory increment fallback. The memory cache is process-local, so one
    // process-wide gate makes its read-modify-write atomic for the only process that can see it.
    private static readonly SemaphoreSlim _incrementGate = new(1, 1);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, ct);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache get failed for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            await _cache.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            }, ct);
            _trackedKeys.TryAdd(key, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache set failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
            _trackedKeys.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache remove failed for key {Key}", key);
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                var value = await db.StringIncrementAsync(key);
                // Set the window TTL only on the bucket's first increment. (A crash between INCR and
                // EXPIRE would leave that one minute's key without a TTL; a Lua INCR+EXPIRE would make
                // it fully atomic — acceptable for a best-effort protective counter.)
                if (value == 1)
                    await db.KeyExpireAsync(key, ttl);
                return value;
            }

            // In-memory fallback: serialize the read-modify-write so concurrent callers can't both
            // read the same value and overshoot the limit.
            await _incrementGate.WaitAsync(ct);
            try
            {
                var next = (await GetAsync<long?>(key, ct) ?? 0) + 1;
                await SetAsync(key, next, ttl, ct);
                return next;
            }
            finally
            {
                _incrementGate.Release();
            }
        }
        catch (Exception ex)
        {
            // Fail open, consistent with Get/Set: a cache outage must not block the guarded action.
            _logger.LogWarning(ex, "Cache increment failed for key {Key}", key);
            return 0;
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var keys = _trackedKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys)
        {
            try
            {
                await _cache.RemoveAsync(key, ct);
                _trackedKeys.TryRemove(key, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache prefix remove failed for key {Key}", key);
            }
        }
    }
}
