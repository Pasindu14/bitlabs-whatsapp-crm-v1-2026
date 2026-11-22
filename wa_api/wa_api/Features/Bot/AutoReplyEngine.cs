using System.Text.RegularExpressions;
using wa_api.Features.Bot.Entities;

namespace wa_api.Features.Bot;

/// <inheritdoc />
public sealed class AutoReplyEngine(ILogger<AutoReplyEngine> logger) : IAutoReplyEngine
{
    /// <summary>
    /// Ceiling on a single user-authored regex evaluation. Rule patterns are tenant input, so an
    /// unbounded match is a denial-of-service vector (catastrophic backtracking) against a Hangfire
    /// worker. A timeout turns that into a logged non-match instead of a pinned thread.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public AutoReplyRule? Match(
        IEnumerable<AutoReplyRule> rules, string? body, bool isFirstInboundInConversation)
    {
        var text = body?.Trim();

        // Ordered here rather than by the caller so every call site gets identical precedence:
        // lowest Priority first, oldest rule breaking a tie.
        var ordered = rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt);

        foreach (var rule in ordered)
        {
            if (Matches(rule, text, isFirstInboundInConversation))
                return rule;
        }

        return null;
    }

    private bool Matches(AutoReplyRule rule, string? text, bool isFirstInboundInConversation) => rule.Trigger switch
    {
        // Position-based triggers ignore Terms entirely.
        AutoReplyTrigger.Welcome => isFirstInboundInConversation,
        AutoReplyTrigger.Fallback => true,

        // Content-based triggers need something to match against. A media-only message stores a
        // placeholder like "[image]", which no sane term should match — but an empty body never does.
        _ when string.IsNullOrWhiteSpace(text) => false,

        AutoReplyTrigger.Exact => rule.Terms.Any(
            t => !string.IsNullOrWhiteSpace(t)
                 && string.Equals(t.Trim(), text, StringComparison.OrdinalIgnoreCase)),

        AutoReplyTrigger.Contains => rule.Terms.Any(
            t => !string.IsNullOrWhiteSpace(t)
                 && text!.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase)),

        AutoReplyTrigger.Keyword => rule.Terms.Any(t => MatchesWholeWord(text!, t)),

        AutoReplyTrigger.Regex => rule.Terms.Any(t => MatchesRegex(rule, text!, t)),

        _ => false,
    };

    /// <summary>
    /// Whole-word containment: "hi" matches "hi there" but not "this". Word boundaries are applied
    /// around an ESCAPED term, so a term containing regex metacharacters is still treated literally.
    /// </summary>
    private bool MatchesWholeWord(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;

        var pattern = $@"(?<!\w){Regex.Escape(term.Trim())}(?!\w)";
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // Escaped literals shouldn't be able to blow the budget, but a pathological input length can.
            logger.LogWarning("Keyword match timed out for term '{Term}' — treated as no match.", term);
            return false;
        }
    }

    /// <summary>
    /// User-authored pattern. Both an invalid pattern and a runaway one degrade to "no match" and a
    /// warning: a broken rule must never stall the reply pipeline or crash the job.
    /// </summary>
    private bool MatchesRegex(AutoReplyRule rule, string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.LogWarning(
                "Regex rule {RuleId} timed out after {Timeout}ms — treated as no match.",
                rule.Id, RegexTimeout.TotalMilliseconds);
            return false;
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex,
                "Regex rule {RuleId} has an invalid pattern — treated as no match.", rule.Id);
            return false;
        }
    }
}
