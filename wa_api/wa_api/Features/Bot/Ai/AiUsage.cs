namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Tokens one or more Anthropic calls consumed, split the way Anthropic prices them.
/// <para>
/// The four buckets do not overlap: <see cref="InputTokens"/> is only the uncached part of the prompt,
/// and the cached prefix is counted in exactly one of <see cref="CacheWriteTokens"/> (stored this call,
/// 1.25x) or <see cref="CacheReadTokens"/> (reused, 0.1x). Dropping the write bucket — as the log did
/// before plan 013 — made every cost computed from it read low, worst on quiet tenants whose cache
/// had expired.
/// </para>
/// </summary>
public sealed record AiUsage(int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens)
{
    public static readonly AiUsage None = new(0, 0, 0, 0);

    public AiUsage Add(AiUsage other) => new(
        InputTokens + other.InputTokens,
        OutputTokens + other.OutputTokens,
        CacheReadTokens + other.CacheReadTokens,
        CacheWriteTokens + other.CacheWriteTokens);

    public bool IsEmpty => InputTokens == 0 && OutputTokens == 0 && CacheReadTokens == 0 && CacheWriteTokens == 0;
}

/// <summary>What <see cref="IAiReplyPlanner.AskAsync"/> returns: the answer (null = do not send) and what it cost.</summary>
public sealed record AiAskResult(string? Text, AiUsage Usage)
{
    public static readonly AiAskResult Unavailable = new(null, AiUsage.None);
}
