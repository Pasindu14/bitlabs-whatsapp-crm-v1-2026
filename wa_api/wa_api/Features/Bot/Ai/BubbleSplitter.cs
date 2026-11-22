namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Turns one model response into the one or two short messages a person would actually have sent.
/// Pure — no I/O, no clock — so the whole contract is unit-testable, exactly like <c>AutoReplyEngine</c>.
/// <para>
/// The prompt asks for this shape; this enforces it. A model that ignores the instruction and writes a
/// six-line paragraph must not put a six-line paragraph on a customer's phone, and a hard cap here is
/// the only guarantee that never happens.
/// </para>
/// </summary>
public static class BubbleSplitter
{
    /// <summary>Most messages one reply may become. Three consecutive sends reads as a bot.</summary>
    public const int MaxBubbles = 2;

    /// <summary>
    /// Ceiling per message. Well under WhatsApp's 4096 limit — this is a readability bound, not a
    /// protocol one. Anything longer has stopped sounding like a person typing on a phone.
    /// </summary>
    public const int MaxBubbleLength = 400;

    /// <summary>
    /// Splits <paramref name="reply"/> on blank lines, then enforces the bubble and length caps.
    /// Returns empty when there is nothing worth sending — the caller treats that as "stay silent".
    /// </summary>
    public static IReadOnlyList<string> Split(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return [];

        var parts = reply
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0) return [];

        // Everything past the cap is folded into the last bubble rather than dropped: the model's
        // closing line is usually the question that moves the sale, and silently discarding it would
        // leave the customer with a statement and nothing to answer.
        if (parts.Count > MaxBubbles)
        {
            var tail = string.Join(" ", parts.Skip(MaxBubbles - 1));
            parts = [.. parts.Take(MaxBubbles - 1), tail];
        }

        return [.. parts.Select(Truncate).Where(p => p.Length > 0)];
    }

    /// <summary>
    /// Trims to the cap at a word boundary when one is close enough, so a clipped message ends on a
    /// word rather than mid-syllable.
    /// </summary>
    private static string Truncate(string part)
    {
        if (part.Length <= MaxBubbleLength) return part;

        var cut = part[..MaxBubbleLength];
        var lastSpace = cut.LastIndexOf(' ');

        // Only honour the word boundary if it is near the end; a message with no spaces in its final
        // quarter (a URL, say) is better clipped cleanly than cut back to a fraction of its length.
        if (lastSpace > MaxBubbleLength * 3 / 4) cut = cut[..lastSpace];

        return cut.TrimEnd() + "…";
    }
}
