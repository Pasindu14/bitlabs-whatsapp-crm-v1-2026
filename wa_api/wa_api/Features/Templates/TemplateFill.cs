using System.Text.RegularExpressions;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates;

/// <summary>What a template needs filling in, and whether it can be.</summary>
/// <param name="Parameters">Body parameters in order. Empty when the template takes none.</param>
/// <param name="RenderedBody">The body with the parameters substituted, for the inbox row.</param>
/// <param name="Problem">Why it cannot be sent, or null when it can.</param>
public sealed record TemplateFillResult(
    IReadOnlyList<string> Parameters,
    string RenderedBody,
    string? Problem
)
{
    public bool CanSend => Problem is null;
}

/// <summary>
/// Turns a template plus some values into something sendable, or refuses.
/// <para>
/// Refusing matters more than filling. Meta rejects a parameter carrying a newline, a tab or more than
/// four consecutive spaces (error 132018), and a template sent with the wrong price is worse than a
/// follow-up nobody receives — the wording was approved, so whatever lands in the variable arrives
/// looking official. So every value is validated, and a bad one stops the send rather than being
/// patched up.
/// </para>
/// </summary>
public static partial class TemplateFill
{
    /// <summary>A parameter this long is a sign something went wrong upstream, not a real value.</summary>
    public const int MaxParameterLength = 120;

    [GeneratedRegex(@"\{\{(\d+)\}\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>How many body variables the template expects.</summary>
    public static int VariableCount(Template template) =>
        PlaceholderRegex().Matches(template.Components.Body.Text)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Builds the parameter list. <paramref name="firstName"/> is always <c>{{1}}</c>;
    /// <paramref name="values"/> supplies <c>{{2}}</c> onward, in order.
    /// </summary>
    public static TemplateFillResult Build(
        Template template, string? firstName, IReadOnlyList<string?> values)
    {
        var needed = VariableCount(template);
        if (needed == 0)
            return new TemplateFillResult([], template.Components.Body.Text, null);

        var parameters = new List<string>(needed);

        for (var i = 1; i <= needed; i++)
        {
            // {{1}} is the customer's name by convention across the whole starter library. "there" is a
            // deliberate fallback rather than a blank: Meta rejects an empty parameter outright, and
            // "Hi there" is a normal thing for a person to write.
            var raw = i == 1
                ? (string.IsNullOrWhiteSpace(firstName) ? "there" : firstName)
                : values.ElementAtOrDefault(i - 2);

            var value = Sanitize(raw);

            if (value is null)
            {
                return new TemplateFillResult(
                    [], template.Components.Body.Text,
                    $"no usable value for {{{{{i}}}}}");
            }

            parameters.Add(value);
        }

        return new TemplateFillResult(parameters, Render(template.Components.Body.Text, parameters), null);
    }

    /// <summary>
    /// A value Meta will accept, or null. Newlines and tabs become spaces rather than a refusal — the
    /// value itself is still the one the owner meant — but a blank or over-long one is refused, because
    /// guessing at those is how a wrong price reaches a customer.
    /// </summary>
    public static string? Sanitize(string? value, int maxLength = MaxParameterLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var collapsed = Regex.Replace(value.Trim(), @"[\r\n\t]+", " ");
        collapsed = Regex.Replace(collapsed, @" {2,}", " ");

        return collapsed.Length == 0 || collapsed.Length > maxLength ? null : collapsed;
    }

    /// <summary>The body with its parameters substituted, so the inbox shows what the customer saw.</summary>
    public static string Render(string body, IReadOnlyList<string> parameters) =>
        PlaceholderRegex().Replace(body, m =>
        {
            var index = int.Parse(m.Groups[1].Value);
            return index >= 1 && index <= parameters.Count ? parameters[index - 1] : m.Value;
        });
}
