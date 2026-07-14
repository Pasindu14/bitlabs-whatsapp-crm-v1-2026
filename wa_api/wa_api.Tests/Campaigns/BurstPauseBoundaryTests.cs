using wa_api.Features.Campaigns.Jobs;
using Xunit;

namespace wa_api.Tests.Campaigns;

/// <summary>
/// Pins the burst-smoothing boundary check that decides whether a chained batch is paused. The pause exists
/// to break a number's continuous send stream into gentler bursts (eases Meta's quality heuristics) without
/// lowering the per-second rate. The boundary is crossed when a batch pushes the campaign's cumulative sent
/// count past a multiple of the configured interval — batch-size independent, so the pause fires roughly
/// every N sends however the batches happen to be sized.
/// </summary>
public class BurstPauseBoundaryTests
{
    [Theory]
    // Crossed within a single 50-send batch: 60→110 passes the 100-boundary.
    [InlineData(60, 110, 100, true)]
    // Landing exactly on a multiple counts as crossing (99→100).
    [InlineData(99, 100, 100, true)]
    // Two batches of 50 accumulate to the boundary on the second one.
    [InlineData(50, 100, 100, true)]
    // A batch fully inside one interval does not fire.
    [InlineData(100, 150, 100, false)]
    [InlineData(0, 50, 100, false)]
    // Exactly on a boundary already, moving forward but not past the next one.
    [InlineData(100, 199, 100, false)]
    // Multiple boundaries crossed by one large batch still fires (single pause).
    [InlineData(90, 260, 100, true)]
    public void CrossedBurstBoundary_FiresOnlyWhenAMultipleIsPassed(
        int before, int after, int every, bool expected)
    {
        Assert.Equal(expected, CampaignBatchSendJob.CrossedBurstBoundary(before, after, every));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CrossedBurstBoundary_Disabled_NeverFires(int every)
    {
        // every <= 0 means the feature is off — no pause regardless of how many were sent.
        Assert.False(CampaignBatchSendJob.CrossedBurstBoundary(0, 100_000, every));
    }

    [Fact]
    public void CrossedBurstBoundary_NoSendsThisBatch_DoesNotFire()
    {
        // A batch that sent nothing (all skipped/failed) leaves the count unchanged — no boundary crossed.
        Assert.False(CampaignBatchSendJob.CrossedBurstBoundary(100, 100, 100));
    }
}
