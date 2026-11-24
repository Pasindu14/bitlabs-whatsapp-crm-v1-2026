using System.Text;
using wa_api.Common.OptOut;
using wa_api.Features.Webhooks.Payloads;

namespace wa_api.Features.ClinicCrm;

/// <summary>
/// Decides whether an inbound WhatsApp message is worth telling the clinic's CRM about. The CRM's staff
/// act on every notification, so only real customer content counts: a question, a photo, a voice note, a
/// location. Opt-outs ("STOP"), reactions, stickers, emoji-only lines, bare acknowledgements ("ok",
/// "thanks") and system events are not.
/// </summary>
public static class ClinicCrmMessageRelevance
{
    /// <summary>Content types that carry something a person must look at, even with no caption.</summary>
    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "location", "contacts", "order",
    };

    /// <summary>
    /// Whole-message acknowledgements that close a thread rather than open one. Matched after stripping
    /// punctuation and emoji, so "Ok 👍" and "Thanks!!" land here too. Kept short on purpose: "yes" and "no"
    /// are often answers to a question and stay meaningful.
    /// </summary>
    private static readonly HashSet<string> Acknowledgements = new(StringComparer.OrdinalIgnoreCase)
    {
        "ok", "okay", "okey", "okk", "k", "kk", "ok thanks", "ok thank you", "okay thanks", "okay thank you",
        "thanks", "thank you", "thankyou", "thank u", "thanks a lot", "thanks so much", "thank you so much",
        "thanx", "thnx", "thx", "ty", "tq", "noted", "cool", "alright", "np", "hmm", "hm", "hmmm",
    };

    public static bool IsMeaningful(WebhookInboundMessage m)
    {
        if (IsStop(m)) return false;

        // Media is real content, caption or not. A sticker is the media version of an emoji.
        if (m.Image is not null || m.Video is not null || m.Audio is not null || m.Document is not null)
            return true;
        if (m.Sticker is not null) return false;

        if (m.Type is not null && ContentTypes.Contains(m.Type)) return true;

        var text = FirstNonBlank(
            m.Text?.Body,
            m.Button?.Text,
            m.Interactive?.ButtonReply?.Title,
            m.Interactive?.ListReply?.Title);

        // Reactions, "unsupported", "system", "request_welcome" and the like carry no text: not meaningful.
        return text is not null && IsMeaningfulText(text);
    }

    /// <summary>True when the text has letters or digits and is not just an acknowledgement.</summary>
    public static bool IsMeaningfulText(string text)
    {
        var words = Normalize(text);
        return words.Length > 0 && !Acknowledgements.Contains(words);
    }

    /// <summary>Letters and digits only, words separated by single spaces, lower case.</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(ch));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>Same channels the webhook checks for an opt-out, so a tapped "Stop" button never notifies.</summary>
    private static bool IsStop(WebhookInboundMessage m) =>
        OptOutKeywords.IsStopIntent(m.Text?.Body)
        || OptOutKeywords.IsStopIntent(m.Button?.Text)
        || OptOutKeywords.IsStopIntent(m.Button?.Payload)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ButtonReply?.Title)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ButtonReply?.Id)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ListReply?.Title)
        || OptOutKeywords.IsStopIntent(m.Interactive?.ListReply?.Id);

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}
