using Microsoft.Extensions.Options;
using wa_api.Infrastructure.RateLimiting;
using Xunit;

namespace wa_api.Tests.RateLimiting;

/// <summary>
/// Verifies the daily unique-recipient tier cap on the in-process limiter (same semantics as the Redis Set
/// path). A generous per-second bucket is configured so only the daily cap is under test here.
/// </summary>
public class InProcessDailyCapTests
{
    private static InProcessTokenBucket NewLimiter() =>
        new(new LocalBucketRegistry(),
            Options.Create(new RateLimitOptions { PerSecondCapacity = 1_000_000, PerSecondRefill = 1_000_000 }));

    [Fact]
    public async Task DistinctRecipients_AllowedUpToTier_ThenRejected()
    {
        var limiter = NewLimiter();
        const int tier = 5;

        for (var i = 0; i < tier; i++)
        {
            var r = await limiter.TryAcquireAsync("num-1", Guid.NewGuid(), tier);
            Assert.True(r.Allowed);
        }

        var over = await limiter.TryAcquireAsync("num-1", Guid.NewGuid(), tier);
        Assert.False(over.Allowed);
        Assert.Equal(RateLimitReason.DailyTierExceeded, over.Reason);
    }

    [Fact]
    public async Task SameRecipient_CountsOnce()
    {
        var limiter = NewLimiter();
        var contact = Guid.NewGuid();
        const int tier = 1;

        // The same recipient may be messaged repeatedly without re-consuming the cap.
        Assert.True((await limiter.TryAcquireAsync("num-2", contact, tier)).Allowed);
        Assert.True((await limiter.TryAcquireAsync("num-2", contact, tier)).Allowed);

        // But a second DISTINCT recipient exceeds the tier-of-1.
        var other = await limiter.TryAcquireAsync("num-2", Guid.NewGuid(), tier);
        Assert.False(other.Allowed);
        Assert.Equal(RateLimitReason.DailyTierExceeded, other.Reason);
    }

    [Fact]
    public async Task SessionMessages_SkipTheDailyCap()
    {
        var limiter = NewLimiter();
        const int tier = 1;

        // null contact (session/service reply) → never counts against the tier.
        for (var i = 0; i < 10; i++)
            Assert.True((await limiter.TryAcquireAsync("num-3", null, tier)).Allowed);

        // contact present but tier <= 0 (Phase-1-style / not business-initiated) → skipped too.
        for (var i = 0; i < 10; i++)
            Assert.True((await limiter.TryAcquireAsync("num-3", Guid.NewGuid(), 0)).Allowed);
    }

    [Fact]
    public async Task SeparateNumbers_TrackTiersIndependently()
    {
        var limiter = NewLimiter();
        const int tier = 1;

        Assert.True((await limiter.TryAcquireAsync("num-a", Guid.NewGuid(), tier)).Allowed);   // fills num-a
        Assert.False((await limiter.TryAcquireAsync("num-a", Guid.NewGuid(), tier)).Allowed);  // num-a full
        Assert.True((await limiter.TryAcquireAsync("num-b", Guid.NewGuid(), tier)).Allowed);   // num-b independent
    }
}
