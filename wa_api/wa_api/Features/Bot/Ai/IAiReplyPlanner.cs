using wa_api.Features.Bot.Ai.Tools;

namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Decides what to say. The only thing in the codebase that talks to Anthropic.
/// </summary>
public interface IAiReplyPlanner
{
    /// <summary>True when an API key is configured. False means the AI path skips itself entirely.</summary>
    bool IsConfigured { get; }

    /// <summary>The model id requests are sent with, so a bill can be reconciled after it changes.</summary>
    string Model { get; }

    /// <summary>
    /// Plans one reply, running whatever tools the model asks for along the way.
    /// <para>
    /// Never throws for an upstream failure: a broken or half-written message on a customer's WhatsApp
    /// is worse than no message, so a failed call comes back as
    /// <see cref="AiReplyPlan.Failed"/> with no bubbles and the caller decides what to do.
    /// </para>
    /// </summary>
    /// <param name="ctx">The business, the clock, and the thread so far.</param>
    /// <param name="tools">Runs tool calls — the live executor, or the preview's dry-run one.</param>
    /// <param name="maxToolIterations">Tool round-trips allowed before the model is forced to answer.</param>
    Task<AiReplyPlan> PlanAsync(
        AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default);

    /// <summary>
    /// One question, one short answer, no tools and no conversation — used to fill a template's variables
    /// from the business details.
    /// <para>
    /// Separate from <see cref="PlanAsync"/> because it is not a reply: nothing it returns is sent to a
    /// customer as written, and the caller validates every value before it goes anywhere near Meta.
    /// A null <see cref="AiAskResult.Text"/> means the model could not be reached, which callers must
    /// treat as "do not send". <see cref="AiAskResult.Usage"/> is what the call cost, for the log.
    /// </para>
    /// <para>Defaulted to unavailable so test fakes that only plan replies need no change.</para>
    /// </summary>
    Task<AiAskResult> AskAsync(string system, string question, int maxTokens, CancellationToken ct = default)
        => Task.FromResult(AiAskResult.Unavailable);
}
