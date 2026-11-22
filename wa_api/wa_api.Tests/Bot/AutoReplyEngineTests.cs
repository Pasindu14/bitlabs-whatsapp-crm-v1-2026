using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.Bot;
using wa_api.Features.Bot.Entities;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins the auto-reply matching contract. The engine is the one part of the chatbot with no I/O, so
/// every precedence and trigger-semantics decision is settled here rather than against a live thread.
/// </summary>
public class AutoReplyEngineTests
{
    private static AutoReplyEngine Engine() => new(NullLogger<AutoReplyEngine>.Instance);

    private static AutoReplyRule Rule(
        AutoReplyTrigger trigger,
        string[]? terms = null,
        int priority = 0,
        string name = "rule",
        bool enabled = true,
        DateTime createdAt = default) => new()
        {
            Name = name,
            Trigger = trigger,
            Terms = [.. terms ?? []],
            ReplyBody = $"reply from {name}",
            Priority = priority,
            IsEnabled = enabled,
            CreatedAt = createdAt == default ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : createdAt,
        };

    // ── Keyword: whole-word containment ───────────────────────────────────────

    [Theory]
    [InlineData("hi")]
    [InlineData("hi there")]
    [InlineData("Oh, HI!")]          // case-insensitive, punctuation is a word boundary
    [InlineData("say hi to them")]
    public void Keyword_MatchesWholeWord(string body)
    {
        var rule = Rule(AutoReplyTrigger.Keyword, ["hi"]);
        Assert.Same(rule, Engine().Match([rule], body, false));
    }

    [Theory]
    [InlineData("this")]             // 'hi' inside a longer word must NOT match
    [InlineData("chihuahua")]
    [InlineData("high")]
    public void Keyword_DoesNotMatchInsideAWord(string body)
    {
        var rule = Rule(AutoReplyTrigger.Keyword, ["hi"]);
        Assert.Null(Engine().Match([rule], body, false));
    }

    [Fact]
    public void Keyword_TreatsRegexMetacharactersLiterally()
    {
        // A term is escaped before word boundaries are applied, so '.' is a dot — not "any char".
        var rule = Rule(AutoReplyTrigger.Keyword, ["c.o.d"]);
        Assert.NotNull(Engine().Match([rule], "is c.o.d available", false));
        Assert.Null(Engine().Match([rule], "is caod available", false));
    }

    // ── Contains / Exact ──────────────────────────────────────────────────────

    [Fact]
    public void Contains_MatchesInsideAWord()
    {
        var rule = Rule(AutoReplyTrigger.Contains, ["hi"]);
        Assert.NotNull(Engine().Match([rule], "this", false));
    }

    [Fact]
    public void Exact_RequiresTheWholeTrimmedMessage()
    {
        var rule = Rule(AutoReplyTrigger.Exact, ["menu"]);
        Assert.NotNull(Engine().Match([rule], "  MENU  ", false));
        Assert.Null(Engine().Match([rule], "show me the menu", false));
    }

    // ── Regex, including hostile patterns ─────────────────────────────────────

    [Fact]
    public void Regex_Matches()
    {
        var rule = Rule(AutoReplyTrigger.Regex, [@"^order\s+\d+$"]);
        Assert.NotNull(Engine().Match([rule], "order 42", false));
        Assert.Null(Engine().Match([rule], "order abc", false));
    }

    [Fact]
    public void Regex_InvalidPattern_IsTreatedAsNoMatchRatherThanThrowing()
    {
        // Patterns are tenant input; a typo must not take down the reply job.
        var rule = Rule(AutoReplyTrigger.Regex, ["("]);
        Assert.Null(Engine().Match([rule], "anything", false));
    }

    [Fact]
    public void Regex_CatastrophicBacktracking_TimesOutInsteadOfHanging()
    {
        // Classic ReDoS shape. The engine caps a single evaluation, so this returns rather than
        // pinning a Hangfire worker.
        var rule = Rule(AutoReplyTrigger.Regex, [@"^(a+)+$"]);
        var body = new string('a', 40) + "!";

        var result = Engine().Match([rule], body, false);

        Assert.Null(result);
    }

    // ── Welcome / Fallback: position, not content ─────────────────────────────

    [Fact]
    public void Welcome_MatchesOnlyTheFirstInboundMessage()
    {
        var rule = Rule(AutoReplyTrigger.Welcome);
        Assert.NotNull(Engine().Match([rule], "whatever", true));
        Assert.Null(Engine().Match([rule], "whatever", false));
    }

    [Fact]
    public void Fallback_MatchesAnything()
    {
        var rule = Rule(AutoReplyTrigger.Fallback);
        Assert.NotNull(Engine().Match([rule], "text nobody planned for", false));
    }

    [Fact]
    public void Fallback_MatchesEvenWhenTheBodyIsBlank()
    {
        // A media-only message can arrive with a placeholder body; Fallback still answers.
        var rule = Rule(AutoReplyTrigger.Fallback);
        Assert.NotNull(Engine().Match([rule], "   ", false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContentTriggers_NeverMatchABlankBody(string? body)
    {
        var rules = new[]
        {
            Rule(AutoReplyTrigger.Keyword, ["hi"]),
            Rule(AutoReplyTrigger.Contains, ["hi"], priority: 1),
            Rule(AutoReplyTrigger.Exact, ["hi"], priority: 2),
            Rule(AutoReplyTrigger.Regex, [".*"], priority: 3),
        };

        Assert.Null(Engine().Match(rules, body, false));
    }

    // ── Precedence ────────────────────────────────────────────────────────────

    [Fact]
    public void LowestPriorityWins_AndFallbackAtHighPriorityIsTheLastResort()
    {
        var fallback = Rule(AutoReplyTrigger.Fallback, priority: 100, name: "fallback");
        var keyword = Rule(AutoReplyTrigger.Keyword, ["price"], priority: 0, name: "price");

        // Order is deliberately reversed on input — the engine sorts, callers don't have to.
        Assert.Same(keyword, Engine().Match([fallback, keyword], "what is the price?", false));
        Assert.Same(fallback, Engine().Match([fallback, keyword], "something else", false));
    }

    [Fact]
    public void EqualPriority_BreaksTieOnOldestRule()
    {
        var older = Rule(AutoReplyTrigger.Fallback, name: "older",
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Rule(AutoReplyTrigger.Fallback, name: "newer",
            createdAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Same(older, Engine().Match([newer, older], "anything", false));
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        var disabled = Rule(AutoReplyTrigger.Fallback, priority: 0, name: "disabled", enabled: false);
        var enabled = Rule(AutoReplyTrigger.Fallback, priority: 1, name: "enabled");

        Assert.Same(enabled, Engine().Match([disabled, enabled], "anything", false));
    }

    [Fact]
    public void NoRules_ReturnsNull() =>
        Assert.Null(Engine().Match([], "anything", true));

    [Fact]
    public void NoMatchingRule_ReturnsNull()
    {
        var rule = Rule(AutoReplyTrigger.Keyword, ["refund"]);
        Assert.Null(Engine().Match([rule], "hello there", false));
    }
}
