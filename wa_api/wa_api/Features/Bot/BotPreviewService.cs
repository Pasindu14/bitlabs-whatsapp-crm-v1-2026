using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Dtos;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Bot.Jobs;
using wa_api.Features.Calendar;
using wa_api.Features.ClinicCrm;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot;

/// <inheritdoc />
public class BotPreviewService(
    AppDbContext db,
    ITenantContext tenant,
    IAiReplyPlanner planner,
    IAvailabilityService availability,
    IAppointmentService appointments,
    IAiUsageLogger usageLog,
    IAiCostCalculator costs,
    ILogger<BotToolExecutor> executorLogger,
    IClinicCrmBookingService? clinicCrm = null) : IBotPreviewService
{
    /// <summary>
    /// Bounded well below the live transcript limit. A preview is for judging the bot's voice, not for
    /// replaying a whole thread, and this endpoint costs a model call per press of the button.
    /// </summary>
    private const int MaxTurns = 20;

    public async Task<BotPreviewResponse> RunAsync(
        BotPreviewRequest request, CancellationToken ct = default)
    {
        if (!planner.IsConfigured)
            throw new BusinessRuleException("AI_NOT_CONFIGURED",
                "The AI assistant is not configured on this server. Contact your administrator.");

        if (request.Turns.Count == 0)
            throw new BusinessRuleException("TURNS_REQUIRED", "Send at least one message to preview.");

        var companyId = tenant.CompanyId
            ?? throw new BusinessRuleException("TENANT_REQUIRED",
                "This endpoint must be called by a company user.");

        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("PROFILE_REQUIRED",
                "Set up the assistant's business profile before previewing it.");

        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var timeZone = CompanyTimeZone.ResolveOrFallback(company?.TimeZoneId);

        var context = await BuildContextAsync(profile, request, timeZone, ct);

        // DryRun: real availability, simulated booking, no send. Note the profile's own IsEnabled is
        // NOT checked — previewing before switching the assistant on is the whole point.
        var executor = new BotToolExecutor(
            db, availability, appointments,
            new BotToolScope(
                CompanyId: companyId,
                ContactId: Guid.Empty,
                ConversationId: null,
                TimeZone: timeZone,
                DryRun: true,
                Transcript: context.Transcript),
            executorLogger,
            clinicCrm);

        var startedAt = DateTime.UtcNow;
        var plan = await planner.PlanAsync(context, executor, AiReplyPlanner.MaxToolIterations, ct);
        var latencyMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

        var outcome = plan.Error is not null ? "Failed"
            : plan.Booked ? "Booked"
            : plan.HandedOff ? "HandedOff"
            : plan.Bubbles.Count == 0 ? "NoReply"
            : "Replied";

        // A preview is real spend with no customer on the other end: logged as Preview so it shows in
        // the cost report but never counts towards the daily reply limit or a company's message costs.
        await usageLog.LogAsync(new AiUsageEntry(
            companyId,
            AiCallKind.Preview,
            planner.Model,
            plan.Usage,
            Enum.Parse<AiReplyOutcome>(outcome),
            LatencyMs: latencyMs,
            Iterations: plan.Iterations,
            ToolCalls: plan.ToolCalls,
            Error: plan.Error), ct);

        // Show the link messages the customer would get after the reply, exactly as they would be sent.
        var linkMessages = plan.Error is null
            ? await BotLinkMessages.BuildMessagesAsync(db, plan.SharedLinkIds, ct)
            : [];

        return new BotPreviewResponse(
            [.. plan.Bubbles, .. linkMessages],
            outcome,
            [.. plan.ToolCalls.Select(c => new BotPreviewToolCall(c.Name, c.Arguments, c.Result))],
            plan.InputTokens,
            plan.OutputTokens,
            latencyMs,
            plan.Error,
            plan.CacheReadTokens,
            plan.CacheWriteTokens,
            costs.CostUsd(planner.Model, plan.Usage));
    }

    public async Task<BotPromptResponse> GetPromptAsync(CancellationToken ct = default)
    {
        var companyId = tenant.CompanyId
            ?? throw new BusinessRuleException("TENANT_REQUIRED",
                "This endpoint must be called by a company user.");

        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("PROFILE_REQUIRED",
                "Set up the assistant's business profile first.");

        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var timeZone = CompanyTimeZone.ResolveOrFallback(company?.TimeZoneId);
        var business = await BotBusinessContextLoader.LoadAsync(db, profile, clinicCrm, ct);

        // A new customer with no name yet, right now — the same per-message part a first reply gets.
        var turn = new AiTurnContext(
            business, timeZone.Id, TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone),
            ContactFirstName: null, Transcript: []);

        var firstScript = BotFollowUpJob.StepsFor(profile)[0].Message;
        var followUp = turn with
        {
            IsFollowUp = true,
            FollowUpScript = string.IsNullOrWhiteSpace(firstScript) ? null : firstScript.Trim(),
        };

        return new BotPromptResponse(
            planner.Model,
            AiReplyPlanner.MaxToolIterations,
            BubbleSplitter.MaxBubbles,
            BubbleSplitter.MaxBubbleLength,
            AiPromptBuilder.BuildStableSystem(business),
            AiPromptBuilder.BuildVolatileSystem(turn),
            AiPromptBuilder.BuildVolatileSystem(followUp),
            AiReplyPlanner.GateInstruction,
            AiReplyPlanner.HoldingMessage,
            [.. BotTools.DefinitionsFor(business.BookingsEnabled).Select(t => new BotPromptTool(t.Name, t.Description ?? string.Empty))]);
    }

    /// <summary>
    /// Same shape the live job builds, from the same tables — so what the tenant tunes here is what
    /// customers get. The only differences are the transcript source and the dry-run tool scope.
    /// </summary>
    private async Task<AiTurnContext> BuildContextAsync(
        BotProfile profile, BotPreviewRequest request, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var business = await BotBusinessContextLoader.LoadAsync(db, profile, clinicCrm, ct);

        var transcript = request.Turns
            .TakeLast(MaxTurns)
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => new AiTranscriptTurn(t.IsCustomer, t.Text.Trim()))
            .ToList();

        return new AiTurnContext(
            business,
            timeZone.Id,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone),
            // No real contact behind a preview, so the prompt correctly says the name is unknown —
            // which also lets a tenant see how the bot asks for it.
            ContactFirstName: null,
            transcript);
    }
}
