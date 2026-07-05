using wa_api.Features.Campaigns.Jobs;
using Xunit;

namespace wa_api.Tests.Campaigns;

/// <summary>
/// Pins the stall-sweeper's poison-batch decision (H2). A deterministic fault makes CampaignBatchSendJob throw
/// every attempt; Hangfire deletes it after its retry budget and the sweeper re-enqueues it. Without a bound
/// that loops forever, never reaching a terminal state or notifying the tenant.
/// <see cref="CampaignSchedulerJob.EvaluateRecovery"/> scores each recovery for progress: a shrinking Queued
/// backlog forgives prior strikes; a stuck backlog accrues them until, past the threshold, the campaign is
/// declared poison (→ CampaignPoisonHandlerJob marks it Failed + notifies) instead of re-enqueued again.
/// </summary>
public class PoisonRecoveryTests
{
    [Fact]
    public void FirstRecovery_ReEnqueues()
    {
        var (attempts, decision) = CampaignSchedulerJob.EvaluateRecovery(
            currentAttempts: 0, lastQueuedCount: null, queuedNow: 100);

        Assert.Equal(1, attempts);
        Assert.Equal(CampaignSchedulerJob.RecoveryDecision.ReEnqueue, decision);
    }

    [Fact]
    public void PoisonBatch_WithNoProgress_EventuallyGivesUp()
    {
        // Backlog never moves: strike count climbs on every recovery until it crosses the threshold, then the
        // sweeper stops re-enqueueing and hands the campaign off as poison.
        int attempts = 0;
        int? lastQueued = null;
        const int stuckBacklog = 100;

        CampaignSchedulerJob.RecoveryDecision decision;
        var reEnqueues = 0;
        do
        {
            (attempts, decision) = CampaignSchedulerJob.EvaluateRecovery(attempts, lastQueued, stuckBacklog);
            lastQueued = stuckBacklog;
            if (decision == CampaignSchedulerJob.RecoveryDecision.ReEnqueue) reEnqueues++;
        }
        while (decision == CampaignSchedulerJob.RecoveryDecision.ReEnqueue && reEnqueues < 100);

        Assert.Equal(CampaignSchedulerJob.RecoveryDecision.Poison, decision);
        Assert.True(reEnqueues < 100, "must reach a terminal Poison decision, not loop forever");
    }

    [Fact]
    public void Progress_ResetsStrikes_SoAHealthyCampaignIsNeverFailed()
    {
        // Each recovery makes progress (backlog shrinks). No matter how many times the job dies to transient
        // infra restarts, a progressing campaign must never be declared poison.
        int attempts = 0;
        int? lastQueued = null;
        var backlog = 500;

        for (var i = 0; i < 20 && backlog > 0; i++)
        {
            backlog -= 50;   // batch sent a chunk before dying
            var (next, decision) = CampaignSchedulerJob.EvaluateRecovery(attempts, lastQueued, backlog);
            attempts = next;
            lastQueued = backlog;

            Assert.Equal(CampaignSchedulerJob.RecoveryDecision.ReEnqueue, decision);
            Assert.Equal(1, attempts);   // progress keeps resetting → strike count stays at 1
        }
    }

    [Fact]
    public void ProgressAfterStrikes_ForgivesAndAvoidsPoison()
    {
        // A batch stalls for a couple of recoveries (strikes accrue), then makes progress. The progress must
        // forgive the accrued strikes so the campaign is not failed on the very next stall.
        int attempts = 0;
        int? lastQueued = null;

        // Two stalled recoveries at backlog 100 → attempts climbs to 2 (below threshold).
        (attempts, _) = CampaignSchedulerJob.EvaluateRecovery(attempts, lastQueued, 100);
        lastQueued = 100;
        (attempts, _) = CampaignSchedulerJob.EvaluateRecovery(attempts, lastQueued, 100);
        lastQueued = 100;
        Assert.Equal(2, attempts);

        // Now progress: backlog drops to 80 → strikes forgiven, back to 1, still re-enqueued.
        var (after, decision) = CampaignSchedulerJob.EvaluateRecovery(attempts, lastQueued, 80);
        Assert.Equal(1, after);
        Assert.Equal(CampaignSchedulerJob.RecoveryDecision.ReEnqueue, decision);
    }
}
