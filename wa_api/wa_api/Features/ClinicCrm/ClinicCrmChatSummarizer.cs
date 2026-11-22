using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.ClinicCrm;

/// <summary>
/// Writes the handover note the clinic sees on a bot booking: the customer's concern, what was discussed,
/// what was recommended — a few lines from the whole chat, never the chat itself (plan 014, at the clinic's
/// request). Read by clinic staff before the visit, so it is in English whatever language the chat was in.
/// </summary>
public interface IClinicCrmChatSummarizer
{
    /// <summary>
    /// The summary, or null when there is nothing to summarise or the model cannot be reached. Null never
    /// stops a booking — it just goes without a note.
    /// </summary>
    Task<string?> SummarizeAsync(Guid companyId, Guid conversationId, string serviceName, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class ClinicCrmChatSummarizer(
    AppDbContext db,
    IAiReplyPlanner planner,
    IAiUsageLogger usageLog,
    ILogger<ClinicCrmChatSummarizer> logger) : IClinicCrmChatSummarizer
{
    /// <summary>More than a reply sees: the concern is usually stated early, the booking happens late.</summary>
    private const int MaxMessages = 40;

    /// <summary>A long message is cut; the gist of it is at the start.</summary>
    private const int MaxMessageLength = 500;

    /// <summary>Under our appointment note limit (1000), leaving room for the bot's own note after it.</summary>
    public const int MaxSummaryLength = 700;

    private const int MaxTokens = 300;

    private const string System =
        """
        You write the handover note a clinic's front desk reads before a customer's appointment. You get the
        WhatsApp chat between the customer and the clinic's assistant that led to the booking.

        Write at most four short lines, in English, each starting with its label, and leave out any line the
        chat gives you nothing for:
        Concern: the customer's problem in their own terms (what, how long, how bad).
        Discussed: what they asked about or were told — treatments, prices, sessions, offers, payment.
        Recommended: what the assistant suggested for them.
        Other: anything else the clinic should know — preferences, worries, who referred them.

        Rules:
        - Only facts stated in the chat. Never guess, diagnose or add medical advice.
        - Do not restate the booking's date, time or service; the clinic already has those.
        - Plain text. No greeting, no headings, no bullet points, no markdown.
        - If the chat holds nothing beyond the booking itself, answer exactly: NONE
        """;

    public async Task<string?> SummarizeAsync(
        Guid companyId, Guid conversationId, string serviceName, CancellationToken ct = default)
    {
        if (!planner.IsConfigured) return null;

        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxMessages)
            .Select(m => new { m.Direction, m.Body })
            .ToListAsync(ct);

        rows.Reverse();

        var transcript = new StringBuilder();
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Body)))
        {
            var body = row.Body!.Trim();
            if (body.Length > MaxMessageLength) body = body[..MaxMessageLength] + "…";

            transcript.Append(row.Direction == MessageDirection.Inbound ? "Customer: " : "Assistant: ")
                      .AppendLine(body.ReplaceLineEndings(" "));
        }

        if (transcript.Length == 0) return null;

        var question = $"The customer has just booked: {serviceName}.\n\nCHAT:\n{transcript}";

        var startedAt = DateTime.UtcNow;
        AiAskResult asked;
        try
        {
            asked = await planner.AskAsync(System, question, MaxTokens, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not summarise conversation {ConversationId} for the clinic.", conversationId);
            return null;
        }

        var summary = Clean(asked.Text);

        // Logged whatever came back — the tokens were spent either way.
        if (!asked.Usage.IsEmpty)
        {
            await usageLog.LogAsync(new AiUsageEntry(
                companyId,
                AiCallKind.BookingSummary,
                planner.Model,
                asked.Usage,
                summary is null ? AiReplyOutcome.NoReply : AiReplyOutcome.Replied,
                ConversationId: conversationId,
                LatencyMs: (int)(DateTime.UtcNow - startedAt).TotalMilliseconds), ct);
        }

        return summary;
    }

    /// <summary>The model's lines, minus any markdown it slipped in, capped. Null for "NONE" or nothing.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Select(l => Markdown().Replace(l, "").Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0 || lines is [var only] && only.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return null;

        var cleaned = string.Join("\n", lines);
        return cleaned.Length <= MaxSummaryLength ? cleaned : cleaned[..(MaxSummaryLength - 1)].TrimEnd() + "…";
    }

    /// <summary>A leading bullet or heading marker (followed by a space), and bold markers.</summary>
    [GeneratedRegex(@"^\s*(?:[-*•#>]+\s+)+|\*\*|__")]
    private static partial Regex Markdown();
}
