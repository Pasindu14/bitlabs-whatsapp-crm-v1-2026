using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Messages;

namespace wa_api.Tests.Bot;

/// <summary>Plan 013: the two calculators every stored cost comes from.</summary>
public class CostCalculatorTests
{
    // ── Claude ────────────────────────────────────────────────────────────────

    [Fact]
    public void AColdCacheReply_IsPricedWithTheCacheWriteRate()
    {
        // The worked example from the pricing doc: 8,700-token prefix stored, 1,000 uncached in, 100 out.
        var cost = TestAiUsage.Calculator().CostUsd("claude-haiku-4-5", new AiUsage(1_000, 100, 0, 8_700));

        Assert.Equal(0.012375m, cost);   // 0.010875 write + 0.001 input + 0.0005 output
    }

    [Fact]
    public void AWarmCacheReply_IsPricedWithTheCacheReadRate()
    {
        var cost = TestAiUsage.Calculator().CostUsd("claude-haiku-4-5", new AiUsage(1_000, 100, 8_700, 0));

        Assert.Equal(0.00237m, cost);    // 0.00087 read + 0.001 input + 0.0005 output
    }

    [Fact]
    public void ModelLookup_IgnoresCase()
    {
        Assert.NotNull(TestAiUsage.Calculator().CostUsd("CLAUDE-HAIKU-4-5", new AiUsage(1, 1, 0, 0)));
    }

    [Fact]
    public void AnUnpricedModel_GivesNull_NotZero()
    {
        // Null lets the report say "unknown" instead of claiming the call was free.
        Assert.Null(TestAiUsage.Calculator().CostUsd("claude-some-future-model", new AiUsage(1_000, 100, 0, 0)));
    }

    [Fact]
    public void UsageAdds_BucketByBucket()
    {
        var total = new AiUsage(1, 2, 3, 4).Add(new AiUsage(10, 20, 30, 40));

        Assert.Equal(new AiUsage(11, 22, 33, 44), total);
        Assert.True(AiUsage.None.IsEmpty);
    }

    // ── Meta ──────────────────────────────────────────────────────────────────

    private static MetaCostCalculator Meta() => new(
        Options.Create(new MetaPricingOptions
        {
            Countries = new()
            {
                ["971"] = new() { Marketing = 0.0499m, Utility = 0.0157m, Authentication = 0.0157m, Service = 0.0157m },
                ["1"] = new() { Marketing = 0.025m, Utility = 0.004m, Authentication = 0.0135m, Service = 0.004m },
                ["1876"] = new() { Marketing = 0.03m, Utility = 0.005m, Authentication = 0.01m, Service = 0.005m },
            },
            Default = new() { Marketing = 0.05m, Utility = 0.02m, Authentication = 0.02m, Service = 0.02m },
        }),
        NullLogger<MetaCostCalculator>.Instance);

    [Theory]
    [InlineData("971501234567", "service", 0.0157)]
    [InlineData("+971 50 123 4567", "marketing", 0.0499)]
    [InlineData("971501234567", "utility", 0.0157)]
    [InlineData("971501234567", "authentication", 0.0157)]
    public void AUaeNumber_IsPricedAtTheUaeRate(string phone, string category, double expected)
    {
        Assert.Equal((decimal)expected, Meta().CostUsd(phone, category, billable: true));
    }

    [Fact]
    public void AnUnlistedCountry_UsesTheDefaultRate()
    {
        Assert.Equal(0.02m, Meta().CostUsd("447700900123", "service", billable: true));
    }

    [Fact]
    public void TheLongestCallingCodeWins()
    {
        // Jamaica (1876) is inside the NANP (1) range but priced on its own.
        Assert.Equal(0.005m, Meta().CostUsd("18765550100", "service", billable: true));
        Assert.Equal(0.004m, Meta().CostUsd("12125550100", "service", billable: true));
    }

    [Fact]
    public void NotBillable_IsFree_AndUnknownBillability_IsNull()
    {
        // Meta's flag covers the 1,000 free service messages per number — we never second-guess it.
        Assert.Equal(0m, Meta().CostUsd("971501234567", "service", billable: false));
        Assert.Null(Meta().CostUsd("971501234567", "service", billable: null));
    }

    [Fact]
    public void AnUnknownBilledCategory_FallsBackToUtility_RatherThanDroppingTheSpend()
    {
        Assert.Equal(0.0157m, Meta().CostUsd("971501234567", "something_new", billable: true));
    }
}
