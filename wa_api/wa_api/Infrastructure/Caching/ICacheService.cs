namespace wa_api.Infrastructure.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the counter at <paramref name="key"/> by one and returns the new value,
    /// setting <paramref name="ttl"/> on first creation. Atomicity (Redis <c>INCR</c>, or a process-wide
    /// guard for the in-memory cache) prevents the read-then-write races that a Get+Set pair allows.
    /// Fails open on a cache outage (returns 0) to preserve graceful degradation.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
