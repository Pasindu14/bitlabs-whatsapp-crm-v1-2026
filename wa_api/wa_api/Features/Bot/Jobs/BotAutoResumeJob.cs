using Microsoft.EntityFrameworkCore;
using wa_api.Features.Conversations.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Jobs;

/// <summary>
/// Hands paused threads back to the bot — either when their takeover window expires, or when the thread
/// has simply gone quiet for long enough.
/// <para>
/// Without this, every agent who replies once silently orphans that conversation: the customer messages
/// again the next day, the bot stays quiet because the thread is still "paused", and nobody notices
/// until the customer gives up. An explicit hand-back button is not enough, because forgetting to press
/// it is the normal case.
/// </para>
/// <para>
/// A pause with no <c>BotResumesAt</c> used to be untouchable, on the reasoning that an escalation must
/// not un-escalate itself. Live, that produced exactly the failure above: a thread paused on 17 Sep
/// swallowed a customer's "Hi" five days later and nobody saw it. So an open-ended pause now expires on
/// SILENCE instead — <c>BotProfile.ResumeBotAfterIdleHours</c> (0 disables it). Silence is the honest
/// signal: while anyone is still talking, the clock keeps resetting and the bot stays out of the way.
/// </para>
/// </summary>
public sealed class BotAutoResumeJob(AppDbContext db, ILogger<BotAutoResumeJob> logger)
{
    /// <summary>Bounded so one sweep cannot hold a worker or a pooled connection indefinitely.</summary>
    private const int BatchSize = 200;

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Runs across every tenant, so the global filter is bypassed deliberately — this is platform
        // plumbing, and each row already carries the company it belongs to.
        var paused = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.BotState == ConversationBotState.Paused
                     && ((c.BotResumesAt != null && c.BotResumesAt <= now)
                         || c.BotResumesAt == null))
            .OrderBy(c => c.LastMessageAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (paused.Count == 0) return;

        // The idle rule is per business, so an open-ended pause needs its owner's setting. One query for
        // the whole batch — paused threads cluster in a handful of companies.
        var companyIds = paused.Where(c => c.BotResumesAt is null).Select(c => c.CompanyId).Distinct().ToList();
        var idleHours = companyIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.BotProfiles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => companyIds.Contains(p.CompanyId))
                .ToDictionaryAsync(p => p.CompanyId, p => p.ResumeBotAfterIdleHours, ct);

        var due = paused.Where(c => c.BotResumesAt is not null || IsIdleLongEnough(c)).ToList();
        if (due.Count == 0) return;

        foreach (var conversation in due)
        {
            conversation.BotState = ConversationBotState.Active;
            conversation.BotPausedAt = null;
            conversation.BotPausedByUserId = null;
            conversation.BotResumesAt = null;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Resumed the bot on {Count} conversations.", due.Count);

        // Quiet since the last message either side sent — and quiet since the pause itself, so a pause
        // placed on an already-old thread still gets its full grace period rather than expiring at once.
        bool IsIdleLongEnough(Conversation c)
        {
            if (!idleHours.TryGetValue(c.CompanyId, out var hours) || hours <= 0) return false;

            var lastActivity = c.BotPausedAt is { } pausedAt && pausedAt > c.LastMessageAt
                ? pausedAt
                : c.LastMessageAt;

            return lastActivity <= now.AddHours(-hours);
        }
    }
}
