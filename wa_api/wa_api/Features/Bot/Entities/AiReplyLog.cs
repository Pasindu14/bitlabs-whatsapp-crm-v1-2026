using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot.Entities;

/// <summary>
/// What the AI engine did for one inbound message, and what it cost.
/// <para>
/// The keyword engine is deterministic: given a rule set and a message you can reproduce its decision
/// on a whiteboard. This one cannot be reproduced at all — the same message can get a different reply
/// tomorrow, and each attempt spends money. Without a row per attempt, neither "why did it say that?"
/// nor "why is the bill that?" is answerable after the fact, so the log is part of the feature rather
/// than instrumentation bolted on later.
/// </para>
/// </summary>
public enum AiReplyOutcome
{
    /// <summary>A reply was sent.</summary>
    Replied,

    /// <summary>The model chose to stay silent, or produced nothing sendable.</summary>
    NoReply,

    /// <summary>The model handed the thread to a human and the bot paused.</summary>
    HandedOff,

    /// <summary>An appointment was booked during the turn — the conversion.</summary>
    Booked,

    /// <summary>The Anthropic call failed. <c>Error</c> carries the reason.</summary>
    Failed,

    /// <summary>The company's daily reply limit was already spent.</summary>
    BudgetExceeded,
}

/// <summary>
/// Why the model was called. Every Anthropic call is logged (plan 013) so the bill adds up, but only
/// <see cref="Reply"/> counts towards a company's daily reply limit — the limit was always about
/// answering customers, and follow-ups or an owner testing the preview must not eat into it.
/// </summary>
public enum AiCallKind
{
    /// <summary>Answering an inbound customer message (<c>AiReplyJob</c>).</summary>
    Reply,

    /// <summary>An in-window nudge after the customer went quiet (<c>BotFollowUpJob</c>).</summary>
    FollowUp,

    /// <summary>Filling a follow-up template's variables from the business details.</summary>
    TemplateVariables,

    /// <summary>The owner testing the assistant from the bot settings page. Real spend, no customer.</summary>
    Preview,

    /// <summary>The chat summary written onto a clinic CRM booking (plan 014).</summary>
    BookingSummary,
}

/// <inheritdoc cref="AiReplyOutcome"/>
public class AiReplyLog : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    public AiCallKind Kind { get; set; } = AiCallKind.Reply;

    /// <summary>Null for a preview, which has no real conversation.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>The inbound message a <see cref="AiCallKind.Reply"/> was answering. Null for the other kinds.</summary>
    public Guid? InboundMessageId { get; set; }

    /// <summary>Model id as sent, so a bill can be reconciled after the configured model changes.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Uncached prompt tokens only — the cached prefix is in one of the two cache columns.</summary>
    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>Cached prefix tokens read. Zero across the board means the cache is silently missing.</summary>
    public int CacheReadTokens { get; set; }

    /// <summary>Cached prefix tokens stored this call (1.25x input). High on quiet tenants whose cache expired.</summary>
    public int CacheWriteTokens { get; set; }

    /// <summary>
    /// USD, computed at write time from <c>Anthropic:Pricing</c> so a later price change never rewrites
    /// history. Null when the model had no configured price.
    /// </summary>
    public decimal? CostUsd { get; set; }

    /// <summary>Wall-clock for the whole planning turn, tool round-trips included.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Tool round-trips taken. Repeatedly hitting the ceiling means the prompt is unclear.</summary>
    public int Iterations { get; set; }

    /// <summary>Tool names and arguments as JSON — the audit trail for a booking the bot made.</summary>
    public string? ToolCallsJson { get; set; }

    public AiReplyOutcome Outcome { get; set; }

    public string? Error { get; set; }
}
