using System.Text.Json;
using wa_api.Features.Bot.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Ai;

/// <summary>One Anthropic spend to record. Everything but the first four is optional context.</summary>
public sealed record AiUsageEntry(
    Guid CompanyId,
    AiCallKind Kind,
    string Model,
    AiUsage Usage,
    AiReplyOutcome Outcome,
    Guid? ConversationId = null,
    Guid? InboundMessageId = null,
    int LatencyMs = 0,
    int Iterations = 0,
    IReadOnlyList<AiToolInvocation>? ToolCalls = null,
    string? Error = null);

public interface IAiUsageLogger
{
    /// <summary>Writes one <see cref="AiReplyLog"/> row, priced, and saves it.</summary>
    Task LogAsync(AiUsageEntry entry, CancellationToken ct = default);
}

/// <summary>
/// The one place an Anthropic call becomes a log row, so every caller — reply, follow-up, template
/// variables, preview — records the same four token buckets and a cost computed the same way. Before
/// plan 013 only replies were logged, and the other calls were spend with no record.
/// </summary>
public sealed class AiUsageLogger(AppDbContext db, IAiCostCalculator costs) : IAiUsageLogger
{
    private const int MaxErrorLength = 2000;

    public async Task LogAsync(AiUsageEntry entry, CancellationToken ct = default)
    {
        db.AiReplyLogs.Add(new AiReplyLog
        {
            CompanyId = entry.CompanyId,
            Kind = entry.Kind,
            ConversationId = entry.ConversationId,
            InboundMessageId = entry.InboundMessageId,
            Model = entry.Model,
            InputTokens = entry.Usage.InputTokens,
            OutputTokens = entry.Usage.OutputTokens,
            CacheReadTokens = entry.Usage.CacheReadTokens,
            CacheWriteTokens = entry.Usage.CacheWriteTokens,
            CostUsd = costs.CostUsd(entry.Model, entry.Usage),
            LatencyMs = entry.LatencyMs,
            Iterations = entry.Iterations,
            ToolCallsJson = entry.ToolCalls is { Count: > 0 } calls ? JsonSerializer.Serialize(calls) : null,
            Outcome = entry.Outcome,
            Error = entry.Error is { Length: > MaxErrorLength } e ? e[..MaxErrorLength] : entry.Error,
        });

        await db.SaveChangesAsync(ct);
    }
}
