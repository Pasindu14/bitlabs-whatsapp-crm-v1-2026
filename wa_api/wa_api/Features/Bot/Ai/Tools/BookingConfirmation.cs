using System.Globalization;
using System.Text.RegularExpressions;

namespace wa_api.Features.Bot.Ai.Tools;

/// <summary>
/// The owner's rule for every booking: send the customer the details, wait for their yes, and only then
/// book. The prompt asks for it; this is the check that makes it true even when the model skips a step.
/// <para>
/// A booking is allowed only when the customer has written since the assistant last spoke, and the
/// assistant's message just before that showed the same day and time. So the details were on the
/// customer's screen and they answered them. Whether the answer was a yes is the model's call, as it is
/// told to book only on a yes.
/// </para>
/// <para>
/// Pure — no I/O — so the whole rule is unit-testable.
/// </para>
/// </summary>
public static class BookingConfirmation
{
    /// <summary>
    /// What the assistant said right before the customer's latest message, joined when it was sent as
    /// several bubbles. Null when the customer has not written since the assistant last spoke (a
    /// follow-up nudge), or when the assistant never said anything.
    /// </summary>
    public static string? LastBotMessageBeforeReply(IReadOnlyList<AiTranscriptTurn> transcript)
    {
        var i = transcript.Count - 1;
        if (i < 0 || !transcript[i].IsCustomer) return null;

        while (i >= 0 && transcript[i].IsCustomer) i--;
        if (i < 0) return null;

        var end = i;
        while (i >= 0 && !transcript[i].IsCustomer) i--;

        return string.Join("\n", transcript.Skip(i + 1).Take(end - i).Select(t => t.Text));
    }

    /// <summary>
    /// True when <paramref name="botText"/> names the day of the month and the time of
    /// <paramref name="startLocal"/>, in any of the usual ways: "Mon 10 Nov, 5:00 PM", "10th at 5pm",
    /// "17:00 on the 10th".
    /// </summary>
    public static bool Shows(string? botText, DateTime startLocal)
    {
        if (string.IsNullOrWhiteSpace(botText)) return false;

        var text = botText.ToLowerInvariant();

        // The day number on its own, not part of a time ("5:00") or another number ("15").
        var day = startLocal.Day.ToString(CultureInfo.InvariantCulture);
        var showsDay = Regex.IsMatch(text, $@"(?<![\d:.]){day}(st|nd|rd|th)?(?!\d|[:.]\d)");

        var hour12 = startLocal.Hour % 12 == 0 ? 12 : startLocal.Hour % 12;
        var minutes = startLocal.Minute.ToString("00", CultureInfo.InvariantCulture);
        var amPm = startLocal.Hour < 12 ? "a\\.?m\\.?" : "p\\.?m\\.?";

        var times = new List<string>
        {
            $@"(?<!\d){hour12}[:.]{minutes}(?!\d)",                       // 5:00, 5.00 (pm optional)
            $@"(?<!\d){startLocal.Hour:00}[:.]{minutes}(?!\d)",            // 17:00, 09:30
        };

        if (startLocal.Minute == 0)
            times.Add($@"(?<!\d){hour12}\s*{amPm}");                        // 5pm, 5 pm, 5 p.m.

        var showsTime = times.Any(p => Regex.IsMatch(text, p));

        return showsDay && showsTime;
    }
}
