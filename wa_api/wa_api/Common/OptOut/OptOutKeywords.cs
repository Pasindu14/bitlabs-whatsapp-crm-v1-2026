namespace wa_api.Common.OptOut;

/// <summary>
/// The canonical WhatsApp opt-out vocabulary, shared by the inbound webhook (which flips a contact to
/// opted-out when it sees one of these) and the template service (which guarantees a matching "Stop"
/// quick-reply button on outgoing marketing templates). Keeping both sides on one list means the button
/// we offer and the keyword we honor can never drift apart.
/// <para>
/// Exact-match only — broad verbs like "cancel" / "end" / "remove" fire on normal conversation
/// ("cancel my order", "end of month works") and would silently suppress active customers. WhatsApp's
/// convention is STOP / UNSUBSCRIBE; stick to that minimal, unambiguous set.
/// </para>
/// </summary>
public static class OptOutKeywords
{
    /// <summary>Default label for the opt-out quick-reply button injected onto marketing templates.</summary>
    public const string StopButtonText = "Stop";

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "stop", "stopall", "stop all", "unsubscribe", "optout", "opt out",
    };

    /// <summary>
    /// True when <paramref name="text"/> IS an opt-out keyword (case-insensitive), tolerating surrounding
    /// whitespace and punctuation so "STOP!", "(stop)", "stop." still register — WhatsApp honours STOP
    /// regardless of trailing punctuation (L2). Interior spaces are preserved ("stop all"). Still a whole-
    /// message match, NOT a substring, so ordinary conversation ("stop by later", "cancel my order") is safe.
    /// </summary>
    public static bool IsStopIntent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim()
            .Trim('.', '!', '?', ',', ';', ':', '"', '\'', '(', ')', '[', ']', '-')
            .Trim();
        return Stop.Contains(normalized);
    }
}
