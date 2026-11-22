using System.Globalization;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.StaffAlerts;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Locking;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Jobs;

/// <summary>
/// Answers one inbound customer message: keyword rules first, then the AI sales engine.
/// <para>
/// Replaces <c>AutoReplyJob</c> at the webhook's enqueue point. Every one of that job's guards is
/// carried over unchanged — they are correct and hard-won, and the AI path is strictly more dangerous
/// than a canned reply, so it inherits all of them plus a bot-paused guard of its own.
/// </para>
/// <para>
/// Rules run first and win. A tenant who wrote a keyword rule gets exactly the reply they wrote; the
/// model only sees the messages that would otherwise have gone unanswered. So nothing that already
/// shipped can regress, and a tenant keeps a deterministic override for anything they care about
/// saying precisely.
/// </para>
/// <para>
/// Like every background job this has no tenant context on entry: the message is read with
/// <c>IgnoreQueryFilters</c>, and the company is only entered
/// (<see cref="ScopedTenantContext.EnterCompany"/>) once it has been derived from the message itself.
/// From that point the calendar services work exactly as they do in a request, which is what lets the
/// bot book through the same code that guards a staff booking.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 60, 300])]
[Queue(wa_api.Common.Extensions.HangfireExtensions.BotQueue)]
public sealed class AiReplyJob(
    AppDbContext db,
    ScopedTenantContext tenant,
    IAutoReplyEngine engine,
    IAiReplyPlanner planner,
    IAvailabilityService availability,
    IAppointmentService appointments,
    IWhatsAppMessageSender sender,
    IChatNotifier notifier,
    IBackgroundJobClient jobs,
    IAiUsageLogger usageLog,
    ILogger<BotToolExecutor> executorLogger,
    ILogger<AiReplyJob> logger,
    IClinicCrmBookingService? clinicCrm = null,
    IDistributedLockService? locks = null,
    IStaffAlertService? staffAlerts = null,
    IBotGate? gate = null)
{

    /// <summary>
    /// Pause between the two bubbles of one reply, scaled by how much there is to "type". Bounded hard:
    /// Hangfire runs four workers against a small connection pool, so a long sleep here is a worker
    /// nobody else can use.
    /// </summary>
    private static readonly TimeSpan MinTypingDelay = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan MaxTypingDelay = TimeSpan.FromSeconds(4);
    private const double CharsPerSecond = 20;

    /// <summary>
    /// A pause before the FIRST message, so the reply does not arrive the instant the customer's
    /// message lands. Claude answers in about 1.5 seconds; nobody reads a message and starts typing
    /// that fast, and the tell is unmistakable on a phone.
    /// <para>
    /// Randomised rather than fixed — a constant gap is its own tell once you have seen it twice.
    /// Deliberately modest: this holds a Hangfire worker (there are four), so it is the first thing to
    /// shorten if reply throughput ever matters more than the illusion.
    /// </para>
    /// </summary>
    private static readonly TimeSpan MinReadDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxReadDelay = TimeSpan.FromSeconds(6);

    private const int PreviewLength = 200;

    /// <summary>How long a job waits before trying again when another one is answering the same conversation.</summary>
    public static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How many turns one job takes for messages that keep arriving while it works. A customer still typing
    /// after this many is answered by the job their next message enqueues.
    /// </summary>
    private const int MaxBurstPasses = 3;

    /// <summary>
    /// How long a job keeps waiting for a busy conversation. The same age past which the webhook itself no
    /// longer hands a message to the bot.
    /// </summary>
    private static readonly TimeSpan MaxBusyWait = TimeSpan.FromMinutes(10);

    public async Task RunAsync(Guid messageId, CancellationToken ct)
    {
        // Not-found is thrown rather than swallowed: the webhook enqueues before its batch commits, so
        // a miss here is a race Hangfire should retry past, not a message that does not exist.
        var message = await db.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId, ct)
            ?? throw new InvalidOperationException($"Message {messageId} not found.");

        // Guard 1 — only customers get answered.
        if (message.Direction != MessageDirection.Inbound) return;

        // Guard 2 — the loop breaker. Never answer our own automated message.
        if (message.IsAutoReply) return;

        // Guard 2a — the chatbot must be on for this company: platform switch, plan, and something set up.
        // The webhook already checks before enqueueing; this re-check covers a job that was queued or
        // scheduled before the bot was switched off (or the plan changed).
        if (gate is not null && !await gate.IsLiveAsync(message.CompanyId, ct))
        {
            logger.LogInformation(
                "Skipping reply to message {MessageId}: the chatbot is not active for company {CompanyId}.",
                message.Id, message.CompanyId);
            return;
        }

        // Guard 2b — too late to answer. The webhook drops messages Meta delivered late; this catches the
        // other cause, a backlog on our side (a campaign's replies arriving faster than the bot's workers
        // clear them). A reply that lands long after the question reads as the business messaging out of
        // nowhere, so the thread is left for a person, who still sees it unread in the inbox.
        if (gate is { Options.MaxReplyAgeMinutes: > 0 } aged
            && DateTime.UtcNow - message.CreatedAt > TimeSpan.FromMinutes(aged.Options.MaxReplyAgeMinutes))
        {
            logger.LogInformation(
                "Skipping reply to message {MessageId}: it has waited {Age} for a worker — left for a person.",
                message.Id, DateTime.UtcNow - message.CreatedAt);
            return;
        }

        if (message.ConversationId is not { } conversationId) return;

        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation is null) return;

        // Guard 3 — replying after a STOP is a Meta policy violation. The inbound handler sets this in
        // the same batch, so the flag is authoritative by the time this runs.
        if (conversation.Contact.IsOptedOut)
        {
            logger.LogInformation(
                "Skipping reply on conversation {ConversationId}: contact has opted out.", conversationId);
            return;
        }

        if (!conversation.WabaConnection.IsActive) return;

        // Guard 4 — re-checked, not assumed: this runs post-commit and may be retried minutes later.
        if (conversation.WindowExpiresAt is null || conversation.WindowExpiresAt <= DateTime.UtcNow)
        {
            logger.LogInformation(
                "Skipping reply on conversation {ConversationId}: 24-hour window closed.", conversationId);
            return;
        }

        // Guard 5 — a human is handling this thread. New for the AI engine and the reason it is safe to
        // leave switched on: a multi-turn model talking over an agent mid-handover is far worse than a
        // one-shot canned reply doing the same.
        if (conversation.BotState == ConversationBotState.Paused)
        {
            logger.LogInformation(
                "Skipping reply on conversation {ConversationId}: bot is paused.", conversationId);
            return;
        }

        // NO wall-clock cooldown here, deliberately — and this is the one guard that differs from
        // AutoReplyJob. A fixed 60-second gag makes sense for a canned rule reply, where ten rapid
        // messages would otherwise become ten identical sends. It is actively wrong for a
        // conversation: answering within a minute is what a conversation IS, so it silently dropped
        // every follow-up a customer sent while still typing at a normal pace.
        //
        // Guard 6 below already does the job properly. It skips when an outbound exists at or after
        // the inbound being answered, so a burst ("hi" / "hello?" / "anyone there?") still gets one
        // reply, while a genuinely new message sent AFTER our reply is answered like a person would.

        // Guard 6 — idempotency, keyed on DIRECTION rather than IsAutoReply. The sender commits the
        // reply row before the caller can stamp IsAutoReply (WhatsAppMessageSender.cs:73-74), so an
        // IsAutoReply-only check would re-send if a crash landed between those two saves. Checking
        // direction closes that window and buys a second property: when a human agent has already
        // replied, the bot stays out of the way.
        if (await HasOutboundSinceAsync(conversationId, message.CreatedAt, ct)) return;

        // Everything below this point reads tenant-scoped data through the normal query filters, so the
        // company is entered here — derived from the message we own, never from anything the customer
        // sent. The guards above deliberately ran before it, on IgnoreQueryFilters reads.
        tenant.EnterCompany(message.CompanyId);

        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(ct);

        // A missing API key or a disabled profile is a supported state, not an outage: the tenant keeps
        // their keyword rules and the bot behaves exactly as it did before this engine existed.
        var aiActive = planner.IsConfigured && profile is { IsEnabled: true };

        // ── Rules first ───────────────────────────────────────────────────────
        // With one exception, and it matters: a Fallback rule matches EVERYTHING, so leaving it in the
        // set would consume every message and the AI would never run for any tenant who has written a
        // catch-all. When the assistant is on, the catch-all is demoted to being its safety net — used
        // only if the model cannot be reached (below). Content triggers still win outright, which is
        // what "rules first" is actually for: saying a specific thing precisely.
        var rules = await db.AutoReplyRules
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == message.CompanyId && r.IsActive && r.IsEnabled
                     && (!aiActive || r.Trigger != AutoReplyTrigger.Fallback))
            .ToListAsync(ct);

        var isFirstInbound = !await db.Messages.IgnoreQueryFilters().AnyAsync(
            m => m.ConversationId == conversationId
              && m.Direction == MessageDirection.Inbound
              && m.CreatedAt < message.CreatedAt, ct);

        if (engine.Match(rules, message.Body, isFirstInbound) is { } rule)
        {
            await SendAsync(conversation, [rule.ReplyBody], ct);
            return;
        }

        if (!aiActive) return;

        await AnswerBurstAsync(message, conversation, profile!, ct);
    }

    /// <summary>
    /// One model turn at a time per conversation, and only for the newest message of a burst.
    /// <para>
    /// Every inbound message enqueues its own job, and the workers run them side by side. Three quick
    /// messages ("book Tuesday" / "10am" / "PRP") used to become three turns that each read the same
    /// transcript and each booked 10am: one won, and the other two told the customer the slot they had
    /// just been given was taken. So an older message leaves the reply to the newest one, and a job that
    /// finds the conversation busy comes back shortly instead of running alongside.
    /// </para>
    /// <para>
    /// The job holding the conversation also answers whatever arrives while it works, because only it
    /// knows which messages its reply had seen. Guard 6 would otherwise drop the later job for a message
    /// the reply never took into account.
    /// </para>
    /// </summary>
    private async Task AnswerBurstAsync(
        Message message, Conversation conversation, BotProfile profile, CancellationToken ct)
    {
        if (await HasInboundAfterAsync(conversation.Id, message.CreatedAt, ct))
        {
            logger.LogInformation(
                "Skipping reply to message {MessageId}: a newer message in conversation {ConversationId} will be answered instead.",
                message.Id, conversation.Id);
            return;
        }

        IAsyncDisposable? handle = null;

        if (locks is not null)
        {
            handle = await locks.AcquireAsync($"ai-reply:{conversation.Id}", ct);

            if (handle is null)
            {
                if (DateTime.UtcNow - message.CreatedAt > MaxBusyWait) return;

                jobs.Schedule<AiReplyJob>(j => j.RunAsync(message.Id, CancellationToken.None), BusyRetryDelay);
                return;
            }
        }

        await using (handle)
        {
            // The answers above were read before the lock was ours; the job that held it may have replied
            // to this very message, or a newer one may have arrived, in between.
            if (handle is not null
                && (await HasOutboundSinceAsync(conversation.Id, message.CreatedAt, ct)
                    || await HasInboundAfterAsync(conversation.Id, message.CreatedAt, ct)))
            {
                return;
            }

            var answering = message;

            for (var pass = 1; ; pass++)
            {
                // What the transcript for this turn will contain. Anything after it arrived mid-turn.
                var seenInboundAt = await NewestInboundAtAsync(conversation.Id, ct) ?? answering.CreatedAt;

                await RunAiAsync(answering, conversation, profile, seenInboundAt, ct);

                if (pass == MaxBurstPasses) break;

                var newer = await NewestInboundAfterAsync(conversation.Id, seenInboundAt, ct);
                if (newer is null) break;

                // Another turn is only ours to take if the bot still has the thread.
                if (!await BotStillHasThreadAsync(conversation.Id, ct)) break;

                answering = newer;
            }
        }
    }

    private Task<bool> HasOutboundSinceAsync(Guid conversationId, DateTime since, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters().AnyAsync(
            m => m.ConversationId == conversationId
              && m.Direction == MessageDirection.Outbound
              && m.CreatedAt >= since, ct);

    private Task<bool> HasInboundAfterAsync(Guid conversationId, DateTime after, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters().AnyAsync(
            m => m.ConversationId == conversationId
              && m.Direction == MessageDirection.Inbound
              && m.CreatedAt > after, ct);

    private Task<Message?> NewestInboundAfterAsync(Guid conversationId, DateTime after, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId
                     && m.Direction == MessageDirection.Inbound
                     && m.CreatedAt > after)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private Task<DateTime?> NewestInboundAtAsync(Guid conversationId, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId && m.Direction == MessageDirection.Inbound)
            .MaxAsync(m => (DateTime?)m.CreatedAt, ct);

    /// <summary>The guards that can change during a turn: a handoff or an agent pausing the bot, and a STOP.</summary>
    private async Task<bool> BotStillHasThreadAsync(Guid conversationId, CancellationToken ct)
    {
        var state = await db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.BotState, c.Contact.IsOptedOut, c.WindowExpiresAt })
            .FirstOrDefaultAsync(ct);

        return state is not null
            && state.BotState != ConversationBotState.Paused
            && !state.IsOptedOut
            && state.WindowExpiresAt > DateTime.UtcNow;
    }

    /// <summary>
    /// Whether the turn changed anything outside the chat. A turn that only looked things up can be thrown
    /// away and taken again; one that booked, moved, cancelled or handed off has to be told to the customer.
    /// </summary>
    private static bool ChangedSomething(AiReplyPlan plan) =>
        plan.Booked || plan.HandedOff || plan.ToolCalls.Any(c =>
            c.Name is not (BotTools.CheckAvailability or BotTools.FindAppointments or BotTools.ShareLink));

    private async Task RunAiAsync(
        Message message, Conversation conversation, BotProfile profile, DateTime seenInboundAt,
        CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == message.CompanyId)
            .Select(c => new { c.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var timeZone = CompanyTimeZone.ResolveOrFallback(company?.TimeZoneId);

        if (await IsOverDailyLimitAsync(profile, timeZone, ct))
        {
            logger.LogWarning(
                "AI reply suppressed for company {CompanyId}: daily limit of {Limit} reached.",
                message.CompanyId, profile.DailyReplyLimit);

            await LogAsync(message, conversation, AiReplyOutcome.BudgetExceeded, null, 0,
                "Daily reply limit reached.", ct);
            return;
        }

        var context = await BuildContextAsync(profile, conversation, timeZone, ct);

        var executor = new BotToolExecutor(
            db, availability, appointments,
            new BotToolScope(
                CompanyId: message.CompanyId,
                ContactId: conversation.ContactId,
                ConversationId: conversation.Id,
                TimeZone: timeZone,
                DryRun: false,
                CustomerText: message.Body,
                Transcript: context.Transcript),
            executorLogger,
            clinicCrm,
            staffAlerts);

        // Tell the inbox the assistant is working. This covers the model call, any tool round-trips
        // and the read delay — several seconds in which the screen would otherwise show nothing.
        // The finally is what matters: every exit path clears it, including a throw, so the indicator
        // cannot be left spinning by a failure.
        await NotifyTypingAsync(conversation, true, ct);

        // And on the customer's phone. Placed after every guard and the daily limit, so it only ever shows
        // when a reply is genuinely coming — which is Meta's condition for using it at all.
        var typing = new CustomerTyping(sender, conversation.WabaConnection, message.ExternalMessageId, logger);
        await typing.ShowAsync(ct);

        try
        {
            var startedAt = DateTime.UtcNow;
            var plan = await planner.PlanAsync(context, executor, AiReplyPlanner.MaxToolIterations, ct);
            var latencyMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (plan.Error is not null)
            {
                await LogAsync(message, conversation, AiReplyOutcome.Failed, plan, latencyMs, plan.Error, ct);

                // The model can fail AFTER the booking is written (the follow-up call that should have
                // produced the reply). The appointment is real, so the customer still gets told.
                if (plan.Booked)
                {
                    await RecordConversionStateAsync(conversation, plan, ct);
                    await SendBookingConfirmationAsync(conversation, profile, timeZone, startedAt, typing, ct);
                    return;
                }

                // Degrade to the tenant's catch-all if they wrote one; otherwise stay silent. A broken
                // or half-written message on a customer's phone is worse than no message.
                await SendFallbackRuleAsync(message, conversation, ct);
                return;
            }

            // The customer wrote again while the model was working, so this reply answers a question
            // they have already moved on from. Nothing was changed, so it is dropped and the turn is
            // taken again with their newer message in view.
            if (!ChangedSomething(plan) && await HasInboundAfterAsync(conversation.Id, seenInboundAt, ct))
            {
                await LogAsync(message, conversation, AiReplyOutcome.NoReply, plan, latencyMs,
                    "Superseded by a newer message.", ct);
                return;
            }

            var linkMessages = await BotLinkMessages.BuildMessagesAsync(db, plan.SharedLinkIds, ct);

            if (plan.Bubbles.Count == 0 && !plan.Booked && linkMessages.Count == 0)
            {
                await LogAsync(message, conversation, AiReplyOutcome.NoReply, plan, latencyMs, null, ct);
                return;
            }

            await RecordConversionStateAsync(conversation, plan, ct);
            if (plan.Bubbles.Count > 0) await SendAsync(conversation, plan.Bubbles, ct, humanPace: true, typing: typing);

            // The confirmation is sent by us, not written by the model: it must arrive every time, and
            // it must state exactly what the calendar holds rather than the model's paraphrase of it.
            if (plan.Booked)
                await SendBookingConfirmationAsync(conversation, profile, timeZone, startedAt, typing, ct);

            // Links the model picked, sent from the stored URL — the model only ever named them.
            if (linkMessages.Count > 0)
            {
                if (plan.Bubbles.Count > 0 || plan.Booked)
                {
                    await typing.ShowAsync(ct);
                    await Task.Delay(MinTypingDelay, ct);
                }

                await SendAsync(conversation, linkMessages, ct, typing: typing);
            }

            // If they go quiet after this, the first step of the follow-up sequence. The job re-checks
            // every precondition on arrival and schedules the steps after it, so scheduling it
            // unconditionally here is safe and keeps the decision in one place.
            var silenceStartedUtc = DateTime.UtcNow;
            jobs.Schedule<BotFollowUpJob>(
                j => j.RunStepAsync(conversation.Id, 0, silenceStartedUtc, CancellationToken.None),
                TimeSpan.FromMinutes(BotFollowUpJob.StepsFor(profile)[0].DelayMinutes));

            var outcome = plan.Booked ? AiReplyOutcome.Booked
                : plan.HandedOff ? AiReplyOutcome.HandedOff
                : AiReplyOutcome.Replied;

            await LogAsync(message, conversation, outcome, plan, latencyMs, null, ct);
        }
        finally
        {
            await NotifyTypingAsync(conversation, false, CancellationToken.None);
        }
    }

    /// <summary>
    /// Persists what this turn did about converting, so the two-attempt limit is a fact on the row
    /// rather than something the model has to re-derive from a transcript that gets trimmed.
    /// <para>
    /// A <c>check_availability</c> call is the proxy for "brought up booking". It is the concrete move
    /// that precedes offering times, and unlike the reply text it is unambiguous in code. It slightly
    /// over-counts — a customer asking "are you open Monday?" can trigger a lookup that was not a sales
    /// attempt — and that error is in the safe direction: it makes the bot back off sooner, never
    /// later, which is the failure the limit exists to prevent.
    /// </para>
    /// </summary>
    private async Task RecordConversionStateAsync(
        Conversation conversation, AiReplyPlan plan, CancellationToken ct)
    {
        var offeredTimes = plan.ToolCalls.Any(c => c.Name == Ai.Tools.BotTools.CheckAvailability);
        if (!offeredTimes && !plan.Booked) return;

        if (plan.Booked)
        {
            // Booked: stop selling, and reset the count so a later conversation on the same thread
            // starts fresh rather than opening with its attempts already spent.
            conversation.BotConvertedAt = DateTime.UtcNow;
            conversation.BotAsksMade = 0;
        }
        else
        {
            conversation.BotAsksMade += 1;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Realtime "the assistant is working" flag. Never fails the job — an indicator is a convenience
    /// and losing it must not cost the customer their reply.
    /// </summary>
    private async Task NotifyTypingAsync(Conversation conversation, bool isTyping, CancellationToken ct)
    {
        try
        {
            await notifier.BotTypingAsync(conversation.CompanyId, conversation.Id, isTyping, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push bot typing state for conversation {ConversationId}", conversation.Id);
        }
    }

    /// <summary>
    /// Has this company already spent its allowance today, counted in ITS OWN day rather than UTC's?
    /// A limit that resets at 05:30 local would be indistinguishable from a bug to the tenant watching it.
    /// </summary>
    private async Task<bool> IsOverDailyLimitAsync(
        BotProfile profile, TimeZoneInfo timeZone, CancellationToken ct)
    {
        if (profile.DailyReplyLimit <= 0) return true;

        var today = CompanyTimeZone.TodayIn(timeZone, DateTime.UtcNow);
        var since = CompanyTimeZone.DayBoundaryToUtc(today, timeZone);

        // Replies only: follow-ups, template variables and previews are logged for cost (plan 013) but
        // were never part of what this limit protects.
        var used = await db.AiReplyLogs.CountAsync(
            l => l.CreatedAt >= since
                 && l.Kind == AiCallKind.Reply
                 && l.Outcome != AiReplyOutcome.BudgetExceeded, ct);

        return used >= profile.DailyReplyLimit;
    }

    private async Task<AiTurnContext> BuildContextAsync(
        BotProfile profile, Conversation conversation, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var business = await BotBusinessContextLoader.LoadAsync(db, profile, clinicCrm, ct);

        var transcript = await LoadTranscriptAsync(conversation, ct);

        // What they actually have booked. "Already booked" used to be BotConvertedAt — set when the bot
        // booked and never cleared — so a customer whose appointment had passed was told they were all set.
        var bookings = await BotAppointmentHistory.LoadAsync(db, conversation.ContactId, timeZone, ct);

        return new AiTurnContext(
            business,
            timeZone.Id,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone),
            FirstName(conversation.Contact.Name),
            transcript,
            AsksMade: conversation.BotAsksMade,
            AlreadyBooked: bookings.Any(b => b.IsUpcoming),
            Bookings: bookings);
    }

    /// <summary>How many recent campaign messages are shown to the model alongside the thread.</summary>
    private const int MaxCampaignContextMessages = 3;

    private async Task<IReadOnlyList<AiTranscriptTurn>> LoadTranscriptAsync(
        Conversation conversation, CancellationToken ct)
    {
        // Newest-first in SQL so the take is index-friendly, then reversed for the model.
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(AiPromptBuilder.MaxTranscriptTurns)
            .Select(m => new { m.Direction, m.Body, m.MediaType, m.CreatedAt })
            .ToListAsync(ct);

        // Campaign sends are not part of any conversation (CampaignBatchSendJob writes them with no
        // ConversationId), so a customer answering a broadcast — "yes, how much?" — would otherwise reach
        // the model with no sign of what they are answering. The recent ones sent to this customer from
        // this number are merged in by time, as things the business said.
        var campaignDays = gate?.Options.CampaignContextDays ?? 7;
        if (campaignDays > 0)
        {
            var since = DateTime.UtcNow.AddDays(-campaignDays);
            var campaignRows = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == null
                         && m.CampaignId != null
                         && m.ContactId == conversation.ContactId
                         && m.WabaConnectionId == conversation.WabaConnectionId
                         && m.Direction == MessageDirection.Outbound
                         && m.Status != MessageStatus.Failed
                         && !m.IsDeleted
                         && m.CreatedAt >= since)
                .OrderByDescending(m => m.CreatedAt)
                .Take(MaxCampaignContextMessages)
                .Select(m => new { m.Direction, m.Body, m.MediaType, m.CreatedAt })
                .ToListAsync(ct);

            if (campaignRows.Count > 0)
            {
                rows = [.. rows.Concat(campaignRows)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(AiPromptBuilder.MaxTranscriptTurns)];
            }
        }

        rows.Reverse();

        return [.. rows.Select(r => new AiTranscriptTurn(
            r.Direction == MessageDirection.Inbound,
            string.IsNullOrWhiteSpace(r.Body)
                ? $"[sent {(r.MediaType is null ? "something" : r.MediaType.ToString()!.ToLowerInvariant())}]"
                : r.Body))];
    }

    /// <summary>
    /// Falls back to the tenant's <c>Fallback</c> rule when the model could not be reached, so a
    /// configured catch-all still answers. Silence is the correct outcome when there is no such rule.
    /// </summary>
    private async Task SendFallbackRuleAsync(Message message, Conversation conversation, CancellationToken ct)
    {
        var fallback = await db.AutoReplyRules
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == message.CompanyId && r.IsActive && r.IsEnabled
                     && r.Trigger == AutoReplyTrigger.Fallback)
            .OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (fallback is null) return;

        await SendAsync(conversation, [fallback.ReplyBody], ct);
    }

    /// <summary>
    /// Sends the reply, one WhatsApp message per bubble, with a typing-shaped pause between them.
    /// <para>
    /// Nothing holds a database connection across the delay — the sender commits each message before it
    /// returns, and the conversation snapshot is written after the last send. That is the same rule
    /// that keeps the Meta call outside the webhook transaction: a pooled connection parked across
    /// network I/O or a sleep is one the small Supavisor cap cannot spare.
    /// </para>
    /// </summary>
    private async Task SendAsync(
        Conversation conversation, IReadOnlyList<string> bubbles, CancellationToken ct,
        bool humanPace = false, CustomerTyping? typing = null)
    {
        // Read-and-think pause before the first message. Only on the AI path: a keyword rule is a
        // canned auto-reply and behaves exactly as it did before this engine existed.
        // Nothing holds a database connection across this — EF closed its connection after the last
        // guard query, and the sender opens its own. That is the same rule that keeps the Meta call
        // outside the webhook transaction.
        if (humanPace)
        {
            // A slow model call can eat most of Meta's 25-second indicator; top it up so it doesn't vanish
            // during the read pause and leave the customer staring at nothing.
            if (typing is not null) await typing.KeepAliveAsync(ct);
            await Task.Delay(ReadDelay(), ct);
        }

        Message? last = null;

        for (var i = 0; i < bubbles.Count; i++)
        {
            if (i > 0)
            {
                // Sending the previous bubble dismissed the indicator; show it again for this one.
                if (typing is not null) await typing.ShowAsync(ct);
                await Task.Delay(TypingDelayFor(bubbles[i]), ct);
            }

            // chargeCredit: false — the window guard above already proved we are inside the 24-hour
            // window, which Meta bills as one conversation however many messages it carries.
            last = await sender.SendAsync(
                conversation.Contact, conversation.WabaConnection, bubbles[i], conversation.Id,
                chargeCredit: false, ct);

            last.IsAutoReply = true;
            typing?.Dismissed();
        }

        if (last is null) return;

        conversation.LastMessageAt = last.CreatedAt;
        conversation.LastMessageBody = Truncate(last.Body, PreviewLength);
        conversation.LastMessageDirection = MessageDirection.Outbound;
        // WindowExpiresAt is deliberately untouched: only an inbound message reopens Meta's window.

        await db.SaveChangesAsync(ct);
        await NotifyAsync(conversation, last, ct);
    }

    /// <summary>
    /// Sends one confirmation per appointment the bot booked during this run, built from the saved
    /// appointment itself. Looked up by source + conversation + time rather than threaded out of the
    /// tool call, so it reflects what was actually committed — a booking the tool reported but the
    /// database refused never gets a confirmation.
    /// </summary>
    private async Task SendBookingConfirmationAsync(
        Conversation conversation, BotProfile profile, TimeZoneInfo timeZone, DateTime since,
        CustomerTyping? typing, CancellationToken ct)
    {
        var booked = await db.Appointments.AsNoTracking()
            .Include(a => a.AppointmentType)
            .Where(a => a.ConversationId == conversation.Id
                     && a.Source == AppointmentSource.Bot
                     && a.Status != AppointmentStatus.Cancelled
                     && a.CreatedAt >= since)
            .OrderBy(a => a.StartUtc)
            .ToListAsync(ct);

        if (booked.Count == 0) return;

        var bubbles = booked.Select(a => BuildConfirmation(a, profile, timeZone)).ToList();

        // A short beat after the chat reply so the two do not land as one block.
        if (typing is not null) await typing.ShowAsync(ct);
        await Task.Delay(MinTypingDelay, ct);
        await SendAsync(conversation, bubbles, ct, typing: typing);
    }

    private static string BuildConfirmation(Appointment appointment, BotProfile profile, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartUtc, timeZone);
        var invariant = CultureInfo.InvariantCulture;

        // Still on its way to the clinic's CRM (plan 014): a request, not a confirmation, until the CRM takes it.
        var pending = appointment.ExternalSyncStatus == ExternalSyncStatus.Pending;

        var sb = new StringBuilder();
        sb.Append(pending ? "🕓 *Booking request received*" : "✅ *Booking confirmed*");
        if (!string.IsNullOrWhiteSpace(profile.BusinessName)) sb.Append(" — ").Append(profile.BusinessName.Trim());
        sb.AppendLine().AppendLine();

        sb.Append("🗓 ").AppendLine(appointment.AppointmentType.Name);
        sb.Append("📅 ").AppendLine(local.ToString("dddd, d MMMM yyyy", invariant));
        sb.Append("🕒 ").AppendLine(local.ToString("h:mm tt", invariant));
        if (!string.IsNullOrWhiteSpace(profile.Location)) sb.Append("📍 ").AppendLine(profile.Location.Trim());

        sb.AppendLine().Append(pending
            ? "The clinic will confirm this shortly."
            : "Need to change or cancel? Just reply here.");
        return sb.ToString();
    }

    /// <summary>
    /// The customer-facing "typing…" for one reply. Meta dismisses it when we send a message or after 25
    /// seconds, so this remembers when it was last shown and whether a send has since cleared it.
    /// <para>
    /// Purely cosmetic: every failure is logged and swallowed, because an indicator that cost the customer
    /// their reply would be the worst possible trade.
    /// </para>
    /// </summary>
    private sealed class CustomerTyping(
        IWhatsAppMessageSender sender, WabaConnection waba, string? inboundWamid, ILogger logger)
    {
        /// <summary>Re-shown well before Meta's 25 s, leaving room for the longest read delay after it.</summary>
        private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(15);

        private DateTime? _shownAt;

        public async Task ShowAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(inboundWamid)) return;

            try
            {
                await sender.ShowTypingAsync(waba, inboundWamid, ct);
                _shownAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not show the typing indicator for {Wamid}.", inboundWamid);
            }
        }

        /// <summary>Re-shows it only if it has been cleared or is close to expiring.</summary>
        public Task KeepAliveAsync(CancellationToken ct) =>
            _shownAt is { } at && DateTime.UtcNow - at < RefreshAfter ? Task.CompletedTask : ShowAsync(ct);

        /// <summary>A message was sent, which removes the indicator on the customer's side.</summary>
        public void Dismissed() => _shownAt = null;
    }

    /// <summary>Uniform inside the read-delay band. Random.Shared is thread-safe.</summary>
    private static TimeSpan ReadDelay() => MinReadDelay +
        TimeSpan.FromMilliseconds(
            Random.Shared.Next(0, (int)(MaxReadDelay - MinReadDelay).TotalMilliseconds));

    private static TimeSpan TypingDelayFor(string bubble)
    {
        var seconds = bubble.Length / CharsPerSecond;
        var delay = TimeSpan.FromSeconds(seconds);

        return delay < MinTypingDelay ? MinTypingDelay
            : delay > MaxTypingDelay ? MaxTypingDelay
            : delay;
    }

    private Task LogAsync(
        Message message, Conversation conversation, AiReplyOutcome outcome,
        AiReplyPlan? plan, int latencyMs, string? error, CancellationToken ct)
        => usageLog.LogAsync(new AiUsageEntry(
            message.CompanyId,
            AiCallKind.Reply,
            planner.Model,
            plan?.Usage ?? AiUsage.None,
            outcome,
            ConversationId: conversation.Id,
            InboundMessageId: message.Id,
            LatencyMs: latencyMs,
            Iterations: plan?.Iterations ?? 0,
            ToolCalls: plan?.ToolCalls,
            Error: error), ct);

    /// <summary>Realtime push so an open inbox sees the bot's reply. Never fails the job.</summary>
    private async Task NotifyAsync(Conversation conversation, Message reply, CancellationToken ct)
    {
        try
        {
            var conversationDto = new ConversationResponse(
                conversation.Id, conversation.ContactId, conversation.Contact.Name, conversation.Contact.Phone,
                conversation.WabaConnectionId, conversation.WabaConnection.DisplayPhoneNumber, conversation.Status,
                conversation.LastMessageAt, conversation.LastMessageBody, conversation.LastMessageDirection,
                conversation.UnreadCount, conversation.WindowExpiresAt, conversation.CreatedAt, conversation.BotState, conversation.BotResumesAt,
                conversation.BotPausedByUserId != null);

            var messageDto = new ConversationMessageResponse(
                reply.Id, conversation.Id, reply.Body, reply.Direction, reply.Status,
                reply.ExternalMessageId, reply.ErrorMessage, reply.CreatedAt, reply.StatusAt,
                reply.MediaType, reply.MediaMimeType, MediaReady: false);

            await notifier.MessageAsync(conversation.CompanyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push bot reply for conversation {ConversationId}", conversation.Id);
        }
    }

    private static string? FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
