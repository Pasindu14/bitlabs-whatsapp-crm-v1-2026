using System.Text.RegularExpressions;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates;

/// <summary>
/// Which templates the chatbot may send on its own (follow-ups and staff handoff alerts).
/// <para>
/// <c>IWhatsAppMessageSender.SendTemplateAsync</c> fills BODY variables only. Campaigns have their own
/// sender that also uploads header media, fills carousel cards and dynamic button URLs — the bot has none
/// of that, so a template needing any of it would be rejected by Meta with a parameter-count error. Such
/// templates are kept out of the bot's pickers and re-checked at send time.
/// </para>
/// </summary>
public static partial class TemplateSendability
{
    [GeneratedRegex(@"\{\{\s*\d+\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// True when every variable the template needs lives in its body: no carousel, no media header, no
    /// variable in a text header and no dynamic button URL.
    /// </summary>
    public static bool IsBodyOnly(Template template) => IsBodyOnly(template.Components);

    public static bool IsBodyOnly(TemplateComponents components)
    {
        if (components.Carousel is not null) return false;

        if (components.Header is { } header)
        {
            var type = header.Type?.Trim().ToLowerInvariant();
            var isText = string.IsNullOrEmpty(type) || type is "none" or "text";
            if (!isText) return false;
            if (!string.IsNullOrEmpty(header.Text) && PlaceholderRegex().IsMatch(header.Text)) return false;
        }

        return !components.Buttons.Any(b => !string.IsNullOrEmpty(b.Url) && PlaceholderRegex().IsMatch(b.Url));
    }
}
