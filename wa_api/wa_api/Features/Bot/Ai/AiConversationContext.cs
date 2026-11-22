namespace wa_api.Features.Bot.Ai;

/// <summary>One bookable service, as the model should describe it.</summary>
public sealed record AiServiceInfo(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price
);

/// <summary>One prepared answer the business wants given verbatim.</summary>
public sealed record AiFaqInfo(string Question, string Answer);

/// <summary>
/// One shareable link, as the model sees it: a title to ask for and a hint for when it fits. The URL
/// is deliberately absent — the job sends it, the model never types it.
/// </summary>
public sealed record AiLinkInfo(string Title, string? WhenToShare);

/// <summary>One line of the weekly opening schedule, already rendered in the company's local time.</summary>
public sealed record AiOpeningHoursLine(string Day, string Hours);

/// <summary>
/// Everything about the business that does not change between messages.
/// <para>
/// The split from <see cref="AiTurnContext"/> is not cosmetic: this half is rendered into the cached
/// system prefix, so it must be byte-identical across turns. Anything that varies — the clock above
/// all — belongs on the turn instead, or the cache silently never hits.
/// </para>
/// </summary>
public sealed record AiBusinessContext(
    string BusinessName,
    string BusinessDescription,
    string? ToneNotes,
    string? Location,
    string? ContactNote,
    string? BookingInstructions,
    string? PreferredLanguage,
    string? CurrencyLabel,
    IReadOnlyList<AiServiceInfo> Services,
    IReadOnlyList<AiFaqInfo> Faqs,
    IReadOnlyList<AiOpeningHoursLine> OpeningHours,
    IReadOnlyList<AiLinkInfo>? Links = null,
    /// <summary>
    /// Rules that only apply to this kind of business, in the owner's words, e.g. a clinic's "never give
    /// medical advice". Rendered as their own section; blank means none.
    /// </summary>
    string? IndustryRules = null,
    /// <summary>
    /// False for a business that does not take bookings through the bot (a shop, an order line). The
    /// booking tools and every booking rule are then left out of the prompt.
    /// </summary>
    bool BookingsEnabled = true
);

/// <summary>One message in the thread, as the model sees it.</summary>
public sealed record AiTranscriptTurn(bool IsCustomer, string Text);

/// <summary>
/// The volatile half: the clock, who is talking, what has been said — and what the assistant has
/// already tried on this thread.
/// <para>
/// <see cref="AsksMade"/> is read from the conversation row rather than inferred from the transcript. The
/// model used to count its own asks by reading back through the history, which silently stopped working
/// once the history was trimmed.
/// </para>
/// <para>
/// <see cref="AlreadyBooked"/> and <see cref="Bookings"/> come from the appointments table
/// (<see cref="BotAppointmentHistory"/>): booked means an upcoming appointment exists, not that the bot
/// once booked something on this thread.
/// </para>
/// </summary>
public sealed record AiTurnContext(
    AiBusinessContext Business,
    string TimeZoneId,
    DateTime NowLocal,
    string? ContactFirstName,
    IReadOnlyList<AiTranscriptTurn> Transcript,
    int AsksMade = 0,
    bool AlreadyBooked = false,
    /// <summary>
    /// This turn is an unprompted nudge after the customer went quiet, not a reply to anything.
    /// Volatile by nature — it is true for exactly one turn.
    /// </summary>
    bool IsFollowUp = false,
    /// <summary>
    /// The customer's upcoming bookings and their most recent past or cancelled ones. Null when there is
    /// no real customer (the settings preview); empty when they have never booked.
    /// </summary>
    IReadOnlyList<AiBookingInfo>? Bookings = null,
    /// <summary>
    /// The owner's script for this follow-up step, when the sequence has one. The model gets its
    /// meaning across in its own words rather than sending it verbatim.
    /// </summary>
    string? FollowUpScript = null
);

/// <summary>A tool the model asked for and what came back, kept for the log and the preview screen.</summary>
public sealed record AiToolInvocation(string Name, string Arguments, string Result);

/// <summary>
/// What the planner decided. <see cref="Bubbles"/> is empty when the model chose to stay silent or
/// produced nothing sendable — the job treats that as a deliberate non-reply, not an error.
/// </summary>
public sealed record AiReplyPlan(
    IReadOnlyList<string> Bubbles,
    IReadOnlyList<AiToolInvocation> ToolCalls,
    bool HandedOff,
    bool Booked,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int Iterations,
    string? Error,
    /// <summary>Links the model asked to share, de-duplicated, in the order asked. Sent by the job.</summary>
    IReadOnlyList<Guid>? SharedLinkIds = null,
    /// <summary>Cached prefix stored this turn (billed at 1.25x). Last, and defaulted, so fakes need no change.</summary>
    int CacheWriteTokens = 0
)
{
    /// <summary>All four token buckets together, the shape the cost calculator and the log take.</summary>
    public AiUsage Usage => new(InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens);

    public static AiReplyPlan Failed(string error) =>
        new([], [], false, false, 0, 0, 0, 0, error);
}
