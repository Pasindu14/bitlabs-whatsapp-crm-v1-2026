using wa_api.Features.Bot.Entities;

namespace wa_api.Features.Bot;

/// <summary>
/// Decides which rule (if any) answers an inbound message. Deliberately pure — no DB, no HTTP, no
/// clock — so the whole matching contract is unit-testable without a fixture.
/// </summary>
public interface IAutoReplyEngine
{
    /// <param name="rules">Candidate rules for one company. Order is irrelevant; the engine sorts.</param>
    /// <param name="body">The inbound message text as stored on the message row.</param>
    /// <param name="isFirstInboundInConversation">
    /// True when this is the customer's first inbound message in the thread — the only thing a
    /// <see cref="AutoReplyTrigger.Welcome"/> rule keys off.
    /// </param>
    /// <returns>The winning rule, or null when nothing matches.</returns>
    AutoReplyRule? Match(IEnumerable<AutoReplyRule> rules, string? body, bool isFirstInboundInConversation);
}
