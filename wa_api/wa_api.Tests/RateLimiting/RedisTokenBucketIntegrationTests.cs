using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using wa_api.Infrastructure.RateLimiting;
using Xunit;

namespace wa_api.Tests.RateLimiting;

/// <summary>
/// Exercises the PRODUCTION path — the actual Redis Lua scripts in <see cref="RedisTokenBucketRateLimiter"/>
/// (the in-process tests cover only the C# twin). Proves the scripts parse, the
/// <c>RedisResult[]</c>/<c>(long)</c> casts hold, and <c>TIME</c>-based refill bounds the rate.
///
/// Gated on the <c>REDIS_TEST_CONNECTION</c> env var so it is a no-op where no Redis is available (local
/// boxes without Docker, CI without a Redis service). To run:
///   docker run --rm -p 6379:6379 redis    then    REDIS_TEST_CONNECTION=localhost:6379 dotnet test
/// </summary>
public class RedisTokenBucketIntegrationTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REDIS_TEST_CONNECTION");

    private static RedisTokenBucketRateLimiter NewLimiter(IConnectionMultiplexer mux, double cap, double refill) =>
        new(mux,
            new LocalBucketRegistry(),
            Options.Create(new RateLimitOptions
            {
                PerSecondCapacity = cap,
                PerSecondRefill = refill,
                FallbackRefill = 5,
                BucketTtlSeconds = 120,
                DailySetTtlSeconds = 90000,
            }),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);

    [Fact]
    public async Task LuaBucket_ParsesAndBoundsRate_UnderParallelLoad()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return; // no Redis available — skip
        using var mux = await ConnectionMultiplexer.ConnectAsync(Conn);
        var phone = $"itest-{Guid.NewGuid():N}";
        var limiter = NewLimiter(mux, cap: 10, refill: 10);

        var granted = 0;
        await Parallel.ForAsync(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (_, _) =>
            {
                var r = await limiter.TryAcquireAsync(phone, null, 0);
                if (r.Allowed) Interlocked.Increment(ref granted);
            });

        // Full bucket (10) plus whatever refilled during the sub-second burst. Bounded — never over-grants.
        Assert.InRange(granted, 10, 30);
    }

    [Fact]
    public async Task LuaDailyCap_ParsesAndEnforcesUniqueRecipientLimit()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return; // no Redis available — skip
        using var mux = await ConnectionMultiplexer.ConnectAsync(Conn);
        var phone = $"itest-{Guid.NewGuid():N}";
        var limiter = NewLimiter(mux, cap: 1_000_000, refill: 1_000_000); // bucket never the bottleneck here
        const int tier = 5;

        for (var i = 0; i < tier; i++)
            Assert.True((await limiter.TryAcquireAsync(phone, Guid.NewGuid(), tier)).Allowed);

        // The (tier+1)-th DISTINCT recipient is rejected — proves the scalar (long) cast + SISMEMBER/SCARD/SADD.
        var over = await limiter.TryAcquireAsync(phone, Guid.NewGuid(), tier);
        Assert.False(over.Allowed);
        Assert.Equal(RateLimitReason.DailyTierExceeded, over.Reason);
    }

    [Fact]
    public async Task LuaDailyCap_SameRecipient_CountsOnce()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return; // no Redis available — skip
        using var mux = await ConnectionMultiplexer.ConnectAsync(Conn);
        var phone = $"itest-{Guid.NewGuid():N}";
        var limiter = NewLimiter(mux, cap: 1_000_000, refill: 1_000_000);
        var contact = Guid.NewGuid();
        const int tier = 1;

        Assert.True((await limiter.TryAcquireAsync(phone, contact, tier)).Allowed);
        Assert.True((await limiter.TryAcquireAsync(phone, contact, tier)).Allowed); // same → idempotent
        Assert.False((await limiter.TryAcquireAsync(phone, Guid.NewGuid(), tier)).Allowed); // distinct → over
    }
}
