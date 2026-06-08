using System.Text.RegularExpressions;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates;

/// <summary>
/// Turns our normalized <see cref="TemplateComponents"/> into the exact JSON shape Meta's
/// <c>POST /{waba-id}/message_templates</c> expects: an ordered <c>components</c> array of
/// HEADER/BODY/FOOTER/BUTTONS objects with their <c>example</c> payloads. Built as nested
/// dictionaries so optional keys are simply omitted (the client serializer ignores nulls).
/// </summary>
public static partial class TemplateMapper
{
    [GeneratedRegex(@"\{\{\d+\}\}")]
    private static partial Regex PlaceholderRegex();

    public static object ToCreatePayload(Template t)
    {
        var c = t.Components;
        var components = new List<object>();

        // ── HEADER (optional) ──────────────────────────────────────────────
        if (c.Header is { } h && h.Type is not ("none" or null))
        {
            var header = new Dictionary<string, object?> { ["type"] = "HEADER" };
            switch (h.Type)
            {
                case "text":
                    header["format"] = "TEXT";
                    header["text"] = h.Text;
                    if (!string.IsNullOrWhiteSpace(h.TextExample))
                        header["example"] = new Dictionary<string, object?> { ["header_text"] = new[] { h.TextExample } };
                    break;
                case "image" or "video" or "document":
                    header["format"] = h.Type.ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(h.MediaHandle))
                        header["example"] = new Dictionary<string, object?> { ["header_handle"] = new[] { h.MediaHandle } };
                    break;
                case "location":
                    header["format"] = "LOCATION";
                    break;
            }
            components.Add(header);
        }

        // ── BODY (mandatory) ───────────────────────────────────────────────
        var body = new Dictionary<string, object?> { ["type"] = "BODY", ["text"] = c.Body.Text };
        if (c.Body.Examples is { Count: > 0 } examples)
            body["example"] = new Dictionary<string, object?> { ["body_text"] = new[] { examples.ToArray() } };
        components.Add(body);

        // ── FOOTER (optional) ──────────────────────────────────────────────
        if (c.Footer is { } f && !string.IsNullOrWhiteSpace(f.Text))
            components.Add(new Dictionary<string, object?> { ["type"] = "FOOTER", ["text"] = f.Text });

        // ── BUTTONS (optional) ─────────────────────────────────────────────
        if (c.Buttons is { Count: > 0 })
        {
            var buttons = new List<object>();
            foreach (var b in c.Buttons)
                buttons.Add(MapButton(b));
            components.Add(new Dictionary<string, object?> { ["type"] = "BUTTONS", ["buttons"] = buttons });
        }

        return new Dictionary<string, object?>
        {
            ["name"] = t.Name,
            ["language"] = t.Language,
            ["category"] = t.Category.ToString().ToUpperInvariant(),   // MARKETING
            ["parameter_format"] = "POSITIONAL",
            ["allow_category_change"] = true,                          // let Meta re-tag rather than reject
            ["components"] = components,
        };
    }

    private static Dictionary<string, object?> MapButton(TemplateButton b)
    {
        var btn = new Dictionary<string, object?> { ["type"] = b.Type.ToUpperInvariant() };
        switch (b.Type)
        {
            case "quick_reply":
                btn["text"] = b.Text;
                break;
            case "url":
                btn["text"] = b.Text;
                btn["url"] = b.Url;
                // Dynamic URLs need an example of the FULLY-RESOLVED url (placeholder substituted).
                if (!string.IsNullOrWhiteSpace(b.Url) && PlaceholderRegex().IsMatch(b.Url!)
                    && !string.IsNullOrWhiteSpace(b.UrlExample))
                    btn["example"] = new[] { PlaceholderRegex().Replace(b.Url!, b.UrlExample!) };
                break;
            case "phone_number":
                btn["text"] = b.Text;
                btn["phone_number"] = b.PhoneNumber;
                break;
            case "copy_code":
                // WhatsApp auto-labels copy-code buttons; only a sample code is sent.
                btn["example"] = string.IsNullOrWhiteSpace(b.Example) ? "REFCODE" : b.Example;
                break;
            default:
                btn["text"] = b.Text;
                break;
        }
        return btn;
    }
}
