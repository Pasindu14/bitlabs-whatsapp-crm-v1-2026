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
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Jobs;

/// <summary>
/// The follow-up sequence when a customer goes quiet mid-conversation — one step per run.
/// <para>
/// <see cref="AiReplyJob"/> schedules step 0 after every reply; each step that sends schedules the next.
/// Every run re-checks everything on arrival, because plenty changes in a few hours: they may have
/// answered, a human may have taken the thread over, the 24-hour window may have closed. Every reply
/// schedules its own step 0, so an early reply's run can fire while a later exchange is still fresh — it
/// checks how long the thread has actually been quiet and leaves it to the later reply's run.
/// </para>
/// <para>
/// INSIDE Meta's 24-hour window the model writes the nudge from the step's script. OUTSIDE it free text
/// is impossible, so the step sends its approved TEMPLATE instead (plan 012) — and a step with no usable
/// template is skipped while the rest of the sequence carries on, because one unapproved template must
/// not silence Day 4 through Day 14.
/// </para>
/// <para>
/// A step due outside the business's opening hours is rescheduled to the next open hour rather than
/// sent. A marketing message landing at 3am is what gets a template reported, and a report costs the
/// number's quality rating.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
[Queue(wa_api.Common.Extensions.HangfireExtensions.BotQueue)]
public sealed class BotFollowUpJob(
    AppDbContext db,
    ScopedTenantContext tenant,
    IAiReplyPlanner planner,
    IAvailabilityService availability,
    IAppointmentService appointments,
    IWhatsAppMessageSender sender,
    TemplateVariableResolver templateVariables,
    IBackgroundJobClient jobs,
    IChatNotifier notifier,
    IAiUsageLogger usageLog,
    ILogger<BotToolExecutor> executorLogger,
    ILogger<BotFollowUpJob> logger,
    IClinicCrmBookingService? clinicCrm = null,
    IStaffAlertService? staffAlerts = null,
    IBotGate? gate = null)
{
    /// <summary>
    /// Slack on the quiet-period check. Hangfire never fires early, but the reply's timestamp is taken a
    /// moment before the run is scheduled — this keeps the run belonging to the LATEST reply from judging
    /// its own silence a few milliseconds short.
    /// </summary>
    private static readonly TimeSpan SilenceTolerance = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Someone who has already been asked twice and then gone silent has answered by not answering.
    /// Chasing a third time is the behaviour the two-attempt limit exists to prevent.
    /// </summary>
    private const int MaxAsksBeforeLettingItGo = 3;

    /// <summary>Longest a step may be delayed — a month of silence is where any sequence should end.</summary>
    public const int MaxStepDelayMinutes = 30 * 24 * 60;

    /// <summary>Wait before the single nudge a business with no follow-up messages gets.</summary>
    public const int DefaultFollowUpMinutes = 6 * 60;

    /// <summary>
    /// The steps this business's customers get. No configured sequence means one nudge after six hours
    /// with no script. Sorted by delay here rather than trusting stored order — a step index only means
    /// anything if every run of this job sees the same list in the same order.
    /// </summary>
    public static IReadOnlyList<BotFollowUpStep> StepsFor(BotProfile profile) =>
        profile.FollowUpSteps is { Count: > 0 } steps
            ? [.. steps
                .Select(s => new BotFollowUpStep
                {
                    DelayMinutes = Math.Clamp(s.DelayMinutes, 1, MaxStepDelayMinutes),
                    Message = s.Message,
                    TemplateId = s.TemplateId,
                    // Null on steps saved before templates existed: their JSON has no such key.
                    VariableOverrides = [.. s.VariableOverrides ?? []],
                })
                .OrderBy(s => s.DelayMinutes)]
            : [new BotFollowUpStep { DelayMinutes = DefaultFollowUpMinutes }];

    /// <summary>
    /// Kept for jobs Hangfire serialised before sequences existed: the single nudge, step 0, with no
    /// anchor to check replies against.
    /// </summary>
    public Task RunAsync(Guid conversationId, CancellationToken ct) =>
        RunStepAsync(conversationId, 0, null, ct);

    /// <param name="conversationId">The quiet thread.</param>
    /// <param name="step">Which step of the sequence this run is for, from 0.</param>
    /// <param name="silenceStartedUtc">
    /// When the assistant's reply that began this silence went out. Every step's delay counts from it,
    /// and any customer message after it means the silence is over.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public async Task RunStepAsync(
        Guid conversationId, int step, DateTime? silenceStartedUtc, CancellationToken ct)
    {
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .Include(c => c.Contact)
            .Include(c => c.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation is null) return;

        // 0. The chatbot must still be on for this company. A follow-up is scheduled hours or days ahead,
        //    so the platform switch or the plan can easily have changed since.
        if (gate is not null && !await gate.IsLiveAsync(conversation.CompanyId, ct)) return;

        // 1. Each step at most once per silence. Cleared when they next write, so a long conversation
        //    with several quiet patches can earn a sequence each — but a step never fires twice, and a
        //    stale job for a step already passed stands down.
        if (conversation.BotFollowUpStepsSent != step) return;
        if (step == 0 && conversation.BotFollowUpSentAt is not null) return;

        // 2. A human owns this thread. Never chase over the top of an agent.
        if (conversation.BotState == ConversationBotState.Paused) return;

        // 3. Opted out, or the connection is gone. Same rules as every other send.
        if (conversation.Contact.IsOptedOut || !conversation.WabaConnection.IsActive) return;

        // 4. The window decides HOW we can speak, not whether we can. Inside it: free text. Outside it:
        //    this step's approved template, or nothing.
        var windowOpen = conversation.WindowExpiresAt is { } expires && expires > DateTime.UtcNow;

        // 5. They replied after all — the silence resolved itself and there is nothing to chase.
        var last = await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (last is null || last.Direction == MessageDirection.Inbound) return;

        // A job from an earlier silence. They wrote since, so a newer reply scheduled its own sequence.
        if (silenceStartedUtc is { } anchor && await db.Messages.IgnoreQueryFilters().AnyAsync(
                m => m.ConversationId == conversationId && !m.IsDeleted
                     && m.Direction == MessageDirection.Inbound && m.CreatedAt > anchor, ct))
        {
            return;
        }

        tenant.EnterCompany(conversation.CompanyId);

        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (!planner.IsConfigured || profile is not { IsEnabled: true }) return;

        var steps = StepsFor(profile);
        if (step >= steps.Count) return;              // the owner shortened the sequence since

        // 5b. Quiet for the full delay. A step-0 run may belong to an EARLIER reply: the conversation
        //     carried on and our latest message is only minutes old. The run scheduled by that latest
        //     reply handles it. Later steps follow our own previous nudge, and the anchor check above
        //     already rules out a conversation that moved on.
        if (step == 0)
        {
            var quietFor = DateTime.UtcNow - last.CreatedAt;
            if (quietFor + SilenceTolerance < TimeSpan.FromMinutes(steps[0].DelayMinutes)) return;
        }

        // 6. Already booked — something is actually coming up for them, so there is nothing to nudge them
        //    towards. Deliberately NOT BotConvertedAt, which is never cleared: a customer booked once last
        //    month, now back and gone quiet, has nothing booked and is exactly who a nudge is for.
        var now = DateTime.UtcNow;
        if (await db.Appointments.AnyAsync(
                a => a.ContactId == conversation.ContactId
                  && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending)
                  && a.StartUtc >= now, ct))
        {
            return;
        }

        // 7. Asked enough already.
        if (conversation.BotAsksMade >= MaxAsksBeforeLettingItGo)
        {
            logger.LogInformation(
                "Not following up on conversation {ConversationId}: {Asks} asks already made.",
                conversationId, conversation.BotAsksMade);
            return;
        }

        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == conversation.CompanyId)
            .Select(c => new { c.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var timeZone = CompanyTimeZone.ResolveOrFallback(company?.TimeZoneId);

        // 8. Opening hours. Nobody wants a marketing message at 3am, and a customer who is annoyed
        //    enough to report one costs the number its quality rating. Held rather than dropped: the
        //    step is still worth sending, just not now.
        if (await NextOpenTimeAsync(conversation.CompanyId, timeZone, ct) is { } openAt)
        {
            logger.LogInformation(
                "Holding follow-up step {Step} on conversation {ConversationId} until {OpenAt} — outside "
                + "opening hours.", step + 1, conversationId, openAt);

            jobs.Schedule<BotFollowUpJob>(
                j => j.RunStepAsync(conversation.Id, step, silenceStartedUtc, CancellationToken.None),
                openAt - DateTimeOffset.UtcNow);

            return;
        }

        // Stamped BEFORE sending. If the send throws and Hangfire retries, a second nudge is worse
        // than none at all — the customer went quiet, and being messaged twice about it is the exact
        // thing that makes a business feel pushy.
        conversation.BotFollowUpSentAt = DateTime.UtcNow;
        conversation.BotFollowUpStepsSent = step + 1;
        await db.SaveChangesAsync(ct);

        ScheduleNextStep(conversation, steps, step, silenceStartedUtc);

        // Outside the window the model cannot help: Meta accepts nothing but an approved template.
        if (!windowOpen)
        {
            await SendTemplateStepAsync(conversation, profile, steps[step], step, ct);
            return;
        }

        var context = await BuildContextAsync(profile, conversation, timeZone, steps[step].Message, ct);

        var executor = new BotToolExecutor(
            db, availability, appointments,
            new BotToolScope(
                conversation.CompanyId, conversation.ContactId, conversation.Id,
                timeZone, DryRun: false, Transcript: context.Transcript),
            executorLogger,
            clinicCrm,
            staffAlerts);

        var startedAt = DateTime.UtcNow;
        var plan = await planner.PlanAsync(context, executor, AiReplyPlanner.MaxToolIterations, ct);

        // Logged whether or not it gets sent — the tokens were spent either way.
        await usageLog.LogAsync(new AiUsageEntry(
            conversation.CompanyId,
            AiCallKind.FollowUp,
            planner.Model,
            plan.Usage,
            plan.Error is not null ? AiReplyOutcome.Failed
                : plan.Bubbles.Count == 0 ? AiReplyOutcome.NoReply
                : plan.Booked ? AiReplyOutcome.Booked
                : AiReplyOutcome.Replied,
            ConversationId: conversation.Id,
            LatencyMs: (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
            Iterations: plan.Iterations,
            ToolCalls: plan.ToolCalls,
            Error: plan.Error), ct);

        // A nudge is worth sending or it is not. No fallback rule, no retry, no second attempt at
        // saying something — unlike a reply, nobody is waiting on this.
        if (plan.Error is not null || plan.Bubbles.Count == 0) return;

        // One message only, whatever the model produced. Two unprompted messages after silence reads
        // as chasing, which is the opposite of the point.
        var body = plan.Bubbles[0];

        // A nudge is one message, so a link the model picked goes in it rather than after it.
        var linkMessages = await BotLinkMessages.BuildMessagesAsync(db, plan.SharedLinkIds, ct);
        if (linkMessages.Count > 0) body = $"{body}\n\n{linkMessages[0]}";

        var reply = await sender.SendAsync(
            conversation.Contact, conversation.WabaConnection, body, conversation.Id,
            chargeCredit: false, ct);

        reply.IsAutoReply = true;

        conversation.LastMessageAt = reply.CreatedAt;
        conversation.LastMessageBody = body.Length <= 200 ? body : body[..200];
        conversation.LastMessageDirection = MessageDirection.Outbound;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Sent follow-up step {Step} on conversation {ConversationId}.", step + 1, conversationId);

        await NotifyAsync(conversation, reply, ct);
    }

    /// <summary>
    /// Realtime push so an open inbox shows the nudge without switching chats. Sent after the save, so the
    /// inbox's refetch sees it. Never fails the job — the message is already out.
    /// </summary>
    private async Task NotifyAsync(Conversation conversation, Message reply, CancellationToken ct)
    {
        try
        {
            var conversationDto = new ConversationResponse(
                conversation.Id, conversation.ContactId, conversation.Contact.Name, conversation.Contact.Phone,
                conversation.WabaConnectionId, conversation.WabaConnection.DisplayPhoneNumber, conversation.Status,
                conversation.LastMessageAt, conversation.LastMessageBody, conversation.LastMessageDirection,
                conversation.UnreadCount, conversation.WindowExpiresAt, conversation.CreatedAt,
                conversation.BotState, conversation.BotResumesAt, conversation.BotPausedByUserId != null);

            var messageDto = new ConversationMessageResponse(
                reply.Id, conversation.Id, reply.Body, reply.Direction, reply.Status,
                reply.ExternalMessageId, reply.ErrorMessage, reply.CreatedAt, reply.StatusAt);

            await notifier.MessageAsync(conversation.CompanyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push follow-up for conversation {ConversationId}", conversation.Id);
        }
    }

    /// <summary>
    /// Sends this step's approved template — the only thing Meta accepts once the window has closed.
    /// <para>
    /// The owner picks the template on the step. A step with none is skipped once the window has closed.
    /// </para>
    /// <para>
    /// Every reason not to send is a log and a return, never an exception: the sequence has already been
    /// advanced and the next step scheduled, so one unusable template costs exactly one message rather
    /// than the rest of the sequence.
    /// </para>
    /// </summary>
    private async Task SendTemplateStepAsync(
        Conversation conversation, Entities.BotProfile profile, BotFollowUpStep step, int stepIndex,
        CancellationToken ct)
    {
        if (step.TemplateId is not { } templateId)
        {
            logger.LogInformation(
                "Follow-up step {Step} on conversation {ConversationId} skipped: the 24-hour window is "
                + "closed and the step has no template.", stepIndex + 1, conversation.Id);
            return;
        }

        var template = await db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);

        // Meta can pause or disable an approved template at any time, so this is checked at send time
        // rather than trusted from when the owner picked it.
        if (template is not null && !TemplateSendability.IsBodyOnly(template))
        {
            // Media-header, carousel and dynamic-URL templates need parameters this sender cannot supply;
            // Meta would reject the send. They are filtered out of the picker, so this is a safety net.
            logger.LogWarning(
                "Follow-up step {Step} on conversation {ConversationId} skipped: template {TemplateId} needs "
                + "header media, a carousel or a dynamic button URL, which the chatbot cannot fill.",
                stepIndex + 1, conversation.Id, templateId);
            return;
        }

        if (template is null || template.Status != TemplateStatus.Approved)
        {
            logger.LogWarning(
                "Follow-up step {Step} on conversation {ConversationId} skipped: template {TemplateId} is "
                + "{Status}.", stepIndex + 1, conversation.Id, templateId,
                template?.Status.ToString() ?? "missing");
            return;
        }

        var business = await BotBusinessContextLoader.LoadAsync(db, profile, clinicCrm, ct);
        var resolution = await templateVariables.ResolveAsync(template, business, step.VariableOverrides, ct);
        var values = resolution.Values;

        // Only when the model was actually asked — typed overrides cost nothing.
        if (!resolution.Usage.IsEmpty)
        {
            await usageLog.LogAsync(new AiUsageEntry(
                conversation.CompanyId,
                AiCallKind.TemplateVariables,
                planner.Model,
                resolution.Usage,
                values.All(v => v is not null) ? AiReplyOutcome.Replied : AiReplyOutcome.NoReply,
                ConversationId: conversation.Id), ct);
        }

        var fill = TemplateFill.Build(template, FirstName(conversation.Contact.Name), values);
        if (!fill.CanSend)
        {
            logger.LogWarning(
                "Follow-up step {Step} on conversation {ConversationId} skipped: {Problem} in template "
                + "{Name}.", stepIndex + 1, conversation.Id, fill.Problem, template.Name);
            return;
        }

        var reply = await sender.SendTemplateAsync(
            conversation.Contact, conversation.WabaConnection, template.Name, template.Language,
            fill.Parameters, fill.RenderedBody, conversation.Id, ct);

        reply.IsAutoReply = true;

        conversation.LastMessageAt = reply.CreatedAt;
        conversation.LastMessageBody = fill.RenderedBody.Length <= 200
            ? fill.RenderedBody
            : fill.RenderedBody[..200];
        conversation.LastMessageDirection = MessageDirection.Outbound;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Sent follow-up step {Step} on conversation {ConversationId} as template {Name}.",
            stepIndex + 1, conversation.Id, template.Name);

        await NotifyAsync(conversation, reply, ct);
    }

    /// <summary>
    /// When the business next opens, or null when it is open right now.
    /// <para>
    /// A company with no opening hours recorded is treated as always open: the alternative is holding a
    /// follow-up forever over a setting the owner never filled in.
    /// </para>
    /// </summary>
    private async Task<DateTimeOffset?> NextOpenTimeAsync(
        Guid companyId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var hours = await db.BusinessHours.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.CompanyId == companyId && h.IsActive)
            .Select(h => new { h.DayOfWeek, h.StartLocal, h.EndLocal })
            .ToListAsync(ct);

        if (hours.Count == 0) return null;

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        // Today first, then the next seven days: the first opening that has not already passed.
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = localNow.Date.AddDays(dayOffset);

            foreach (var slot in hours
                .Where(h => h.DayOfWeek == date.DayOfWeek)
                .OrderBy(h => h.StartLocal))
            {
                var opens = date + slot.StartLocal.ToTimeSpan();
                var closes = date + slot.EndLocal.ToTimeSpan();

                if (dayOffset == 0 && localNow >= opens && localNow < closes) return null;   // open now
                if (opens > localNow) return new DateTimeOffset(opens, timeZone.GetUtcOffset(opens));
            }
        }

        // Hours exist but none of them are ahead of us, which should not happen. Sending beats hanging.
        return null;
    }

    /// <summary>
    /// Queues the step after <paramref name="step"/>.
    /// <para>
    /// It no longer stops at the 24-hour window: that refusal was the whole reason the client's Day 2 to
    /// Day 14 scripts never sent. A step past the window goes out as its template, or is skipped on
    /// arrival if it has none.
    /// </para>
    /// </summary>
    private void ScheduleNextStep(
        Conversation conversation, IReadOnlyList<BotFollowUpStep> steps, int step, DateTime? silenceStartedUtc)
    {
        var next = step + 1;
        if (next >= steps.Count) return;

        var anchor = silenceStartedUtc ?? DateTime.UtcNow;
        var dueUtc = anchor.AddMinutes(steps[next].DelayMinutes);

        // A step past the window needs its template. Scheduled anyway:
        // the template is checked on arrival, where "is it still Approved" is a question only the clock
        // can answer, and a step with nothing usable is skipped there while the rest carries on.
        var delay = dueUtc - DateTime.UtcNow;
        jobs.Schedule<BotFollowUpJob>(
            j => j.RunStepAsync(conversation.Id, next, anchor, CancellationToken.None),
            delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The same context a reply gets, with <c>IsFollowUp</c> set so the prompt knows nothing new has
    /// been said and it must not answer or repeat itself.
    /// </summary>
    private async Task<AiTurnContext> BuildContextAsync(
        BotProfile profile, Conversation conversation, TimeZoneInfo timeZone, string? script,
        CancellationToken ct)
    {
        var transcript = await LoadTranscriptAsync(conversation.Id, ct);

        var business = await BotBusinessContextLoader.LoadAsync(db, profile, clinicCrm, ct);

        return new AiTurnContext(
            business,
            timeZone.Id,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone),
            FirstName(conversation.Contact.Name),
            transcript,
            AsksMade: conversation.BotAsksMade,
            AlreadyBooked: false,
            IsFollowUp: true,
            Bookings: await BotAppointmentHistory.LoadAsync(db, conversation.ContactId, timeZone, ct),
            FollowUpScript: string.IsNullOrWhiteSpace(script) ? null : script.Trim());
    }

    /// <summary>The recent thread in the shape the model reads, oldest first.</summary>
    private async Task<List<AiTranscriptTurn>> LoadTranscriptAsync(Guid conversationId, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(AiPromptBuilder.MaxTranscriptTurns)
            .Select(m => new { m.Direction, m.Body })
            .ToListAsync(ct);

        rows.Reverse();

        return [.. rows.Select(r => new AiTranscriptTurn(
            r.Direction == MessageDirection.Inbound, r.Body ?? "[media]"))];
    }

    private static string? FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }
}
