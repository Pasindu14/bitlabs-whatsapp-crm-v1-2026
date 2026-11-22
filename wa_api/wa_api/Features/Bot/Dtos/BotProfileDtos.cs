using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Bot.Dtos;

/// <summary>
/// The AI assistant's configuration as returned to the settings screen. <see cref="Exists"/> is false
/// when the tenant has never saved one — the screen renders empty defaults rather than a 404, because
/// "not configured yet" is the normal state for every company on day one.
/// </summary>
public record BotProfileResponse(
    Guid? Id,
    bool Exists,
    bool IsEnabled,
    string BusinessName,
    string BusinessDescription,
    string? ToneNotes,
    string? Location,
    string? ContactNote,
    string? BookingInstructions,
    string? PreferredLanguage,
    string? CurrencyLabel,
    int DailyReplyLimit,
    DateTime? UpdatedAt,
    IReadOnlyList<BotFollowUpStepDto> FollowUpSteps,
    int ResumeBotAfterIdleHours,
    IReadOnlyList<string> HandoffAlertPhones,
    Guid? HandoffAlertTemplateId,
    int HandoffResumeMinutes,
    string? IndustryRules,
    bool BookingsEnabled
);

/// <summary>
/// One step of the follow-up sequence. <c>DelayMinutes</c> counts from the assistant's last reply, not
/// from the step before, so "Day 4" is 5760 whatever the earlier steps are set to.
/// </summary>
public record BotFollowUpStepDto(
    [Range(1, 43200)] int DelayMinutes,
    [Required, MaxLength(1000)] string Message,
    /// <summary>The approved template used once this step falls outside the 24-hour window.</summary>
    Guid? TemplateId = null,
    /// <summary>Fixed values from {{2}} onward. Blank entries are resolved from the profile.</summary>
    [MaxLength(10)] IReadOnlyList<string>? VariableOverrides = null
);

/// <summary>
/// Body for PUT /bot/profile. Upsert, not create-or-update: there is exactly one profile per company,
/// so the client never has to know whether it exists.
/// </summary>
public record SaveBotProfileRequest(
    bool IsEnabled,
    [Required, MaxLength(200)] string BusinessName,
    [Required, MaxLength(2000)] string BusinessDescription,
    [MaxLength(500)] string? ToneNotes,
    [MaxLength(500)] string? Location,
    [MaxLength(500)] string? ContactNote,
    [MaxLength(1000)] string? BookingInstructions,
    [MaxLength(60)] string? PreferredLanguage,
    [MaxLength(10)] string? CurrencyLabel,
    [Range(0, 100000)] int DailyReplyLimit,
    /// <summary>Null leaves the saved sequence alone; empty clears it back to the single nudge.</summary>
    [MaxLength(10)] IReadOnlyList<BotFollowUpStepDto>? FollowUpSteps = null,
    /// <summary>Hours of silence before a paused thread returns to the bot. 0 = never.</summary>
    [Range(0, 720)] int ResumeBotAfterIdleHours = 24,
    /// <summary>Team WhatsApp numbers told about a handoff. Null leaves the saved list alone.</summary>
    IReadOnlyList<string>? HandoffAlertPhones = null,
    /// <summary>Approved three-variable template for those alerts. Null turns the WhatsApp alert off.</summary>
    Guid? HandoffAlertTemplateId = null,
    /// <summary>Minutes before the bot resumes after handing a chat to the team. 0 = only when handed back.</summary>
    [Range(0, 10080)] int HandoffResumeMinutes = 0,
    /// <summary>Rules for this kind of business only. Null leaves the saved rules alone; empty clears them.</summary>
    [MaxLength(2000)] string? IndustryRules = null,
    /// <summary>Whether the bot books appointments. Null leaves the saved setting alone.</summary>
    bool? BookingsEnabled = null
);

public record BotFaqResponse(
    Guid Id,
    string Question,
    string Answer,
    int SortOrder,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SaveBotFaqRequest(
    [Required, MaxLength(500)] string Question,
    [Required, MaxLength(2000)] string Answer,
    int SortOrder,
    bool IsEnabled
);

public record BotLinkResponse(
    Guid Id,
    string Title,
    string Url,
    string? WhenToShare,
    string? Message,
    int SortOrder,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SaveBotLinkRequest(
    [Required, MaxLength(100)] string Title,
    [Required, MaxLength(2000)] string Url,
    [MaxLength(500)] string? WhenToShare,
    [MaxLength(300)] string? Message,
    int SortOrder,
    bool IsEnabled
);

/// <summary>One turn of a dry-run transcript. <c>IsCustomer</c> false means the assistant said it.</summary>
public record BotPreviewTurn(
    bool IsCustomer,
    [Required, MaxLength(4096)] string Text
);

/// <summary>
/// Body for POST /bot/preview — a conversation to run against the live profile without sending
/// anything. The last turn should be the customer's.
/// </summary>
public record BotPreviewRequest(
    [Required] IReadOnlyList<BotPreviewTurn> Turns
);

/// <summary>
/// What the model would have replied, plus what it would have done. Tool calls are surfaced so a
/// tenant tuning the persona can see the bot reaching for availability rather than guessing at it.
/// </summary>
public record BotPreviewResponse(
    IReadOnlyList<string> Bubbles,
    string Outcome,
    IReadOnlyList<BotPreviewToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    string? Error,
    int CacheReadTokens = 0,
    int CacheWriteTokens = 0,
    /// <summary>USD this preview cost. Null when the model has no configured price.</summary>
    decimal? CostUsd = null
);

public record BotPreviewToolCall(
    string Name,
    string Arguments,
    string Result
);

/// <summary>
/// What the model is actually given for this business — for the read-only "AI prompt" tab.
/// </summary>
public record BotPromptResponse(
    string Model,
    int MaxToolIterations,
    int MaxMessages,
    int MaxMessageLength,
    /// <summary>The cached part: rules plus everything about the business (AiPromptBuilder).</summary>
    string StableSystem,
    /// <summary>The part rebuilt for every message: the clock, the dates, the customer's name.</summary>
    string VolatileSystem,
    /// <summary>The per-message part when the turn is a follow-up nudge, using the first step's script.</summary>
    string FollowUpVolatileSystem,
    /// <summary>The separate "can I answer this?" check run before every reply (AiReplyPlanner).</summary>
    string GateInstruction,
    /// <summary>What the customer is sent when the chat is handed to the team.</summary>
    string HoldingMessage,
    IReadOnlyList<BotPromptTool> Tools
);

public record BotPromptTool(string Name, string Description);
