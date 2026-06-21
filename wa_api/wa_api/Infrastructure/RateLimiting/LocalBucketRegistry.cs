using System.Collections.Concurrent;
using System.Diagnostics;

namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// Process-local token buckets keyed by an arbitrary string. The pure in-memory token-bucket math shared
/// by <see cref="InProcessTokenBucket"/> (primary, no-Redis mode) and the fail-safe fallback path inside
/// <see cref="RedisTokenBucketRateLimiter"/>. Registered as a singleton so state survives across scoped
/// limiter instances. Uses <see cref="Stopwatch"/> ticks (monotonic) so it is immune to wall-clock changes.
/// </summary>
public sealed class LocalBucketRegistry
{
    private sealed class Bucket
    {
        public double Tokens;
        public long Ticks;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>Attempt to take one token from <paramref name="key"/>'s bucket. Thread-safe per key.</summary>
    public RateLimitResult TryTake(string key, double capacity, double refillPerSec)
    {
        if (refillPerSec <= 0) refillPerSec = 1; // guard against misconfiguration / divide-by-zero

        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { Tokens = capacity, Ticks = Stopwatch.GetTimestamp() });
        lock (bucket)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - bucket.Ticks) / (double)Stopwatch.Frequency;
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * refillPerSec);
            bucket.Ticks = now;

            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return RateLimitResult.Allow;
            }

            var retryMs = (int)Math.Ceiling((1 - bucket.Tokens) / refillPerSec * 1000);
            return new RateLimitResult(false, TimeSpan.FromMilliseconds(retryMs), RateLimitReason.PerSecondThrottle);
        }
    }
}
