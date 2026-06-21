using wa_api.Infrastructure.RateLimiting;
using Xunit;

namespace wa_api.Tests.RateLimiting;

/// <summary>
/// Proves the token-bucket math bounds the grant rate — the safety property the whole feature exists for.
/// These exercise <see cref="LocalBucketRegistry"/>, the in-memory bucket shared by the no-Redis limiter and
/// the Redis outage fallback. The Redis Lua path implements the same algorithm and is covered separately by
/// an integration test that requires a live Redis.
/// </summary>
public class LocalBucketRegistryTests
{
    [Fact]
    public void FullBucket_GrantsExactlyCapacity_OnImmediateBurst()
    {
        var registry = new LocalBucketRegistry();
        const double capacity = 10, refill = 10; // 10/s — negligible refill across a sub-ms burst

        var granted = 0;
        for (var i = 0; i < 100; i++)
            if (registry.TryTake("num-a", capacity, refill).Allowed)
                granted++;

        // A full bucket yields exactly its capacity before any meaningful refill elapses.
        Assert.InRange(granted, 10, 11);
    }

    [Fact]
    public async Task ParallelContention_NeverOverGrants()
    {
        // THE critical test: many threads hammering one number must not collectively exceed the bucket.
        var registry = new LocalBucketRegistry();
        const double capacity = 10, refill = 10;

        var granted = 0;
        await Parallel.ForAsync(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 32 },
            (_, _) =>
            {
                if (registry.TryTake("num-b", capacity, refill).Allowed)
                    Interlocked.Increment(ref granted);
                return ValueTask.CompletedTask;
            });

        // 500 concurrent attempts, full bucket = 10. The lock makes the read-modify-write atomic, so grants
        // are capacity + whatever refilled during the run. The upper bound is generous (refill can elapse on a
        // slow/loaded CI box) but far below the ~500 a broken lock would grant — so it still catches the bug
        // it guards while staying flake-resistant.
        Assert.InRange(granted, 10, 40);
    }

    [Fact]
    public async Task EmptyBucket_RefillsOverTime()
    {
        var registry = new LocalBucketRegistry();
        const double capacity = 5, refill = 20; // 20/s → ~50ms per token

        // Drain it.
        for (var i = 0; i < 5; i++) Assert.True(registry.TryTake("num-c", capacity, refill).Allowed);

        // Immediately empty.
        var denied = registry.TryTake("num-c", capacity, refill);
        Assert.False(denied.Allowed);
        Assert.Equal(RateLimitReason.PerSecondThrottle, denied.Reason);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);

        // After enough time for several tokens to refill, a permit is available again.
        await Task.Delay(250);
        Assert.True(registry.TryTake("num-c", capacity, refill).Allowed);
    }

    [Fact]
    public void SeparateNumbers_HaveIndependentBuckets()
    {
        var registry = new LocalBucketRegistry();
        const double capacity = 1, refill = 1;

        Assert.True(registry.TryTake("num-x", capacity, refill).Allowed);  // drains x
        Assert.False(registry.TryTake("num-x", capacity, refill).Allowed); // x now empty
        Assert.True(registry.TryTake("num-y", capacity, refill).Allowed);  // y is untouched
    }
}
