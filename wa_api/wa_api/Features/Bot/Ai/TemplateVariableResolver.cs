using System.Text;
using System.Text.Json;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Works out what goes in a template's variables, from the business's own details.
/// <para>
/// The order is: the owner's typed override, then the model, then refuse. Refusing is a real outcome and
/// the point of the class — the wording around a variable was approved by Meta, so whatever lands inside
/// it arrives looking official. A price the model guessed at would be indistinguishable, to the customer,
/// from a price the clinic set.
/// </para>
/// <para>
/// Everything the model returns is validated by <see cref="TemplateFill"/> before use: single line,
/// length-capped, non-empty. One value it cannot establish stops the whole send.
/// </para>
/// </summary>
public sealed class TemplateVariableResolver(IAiReplyPlanner planner, ILogger<TemplateVariableResolver> logger)
{
    /// <summary>Short on purpose: the answer is a handful of words per variable, not prose.</summary>
    private const int MaxTokens = 300;

    /// <summary>
    /// Values for <c>{{2}}</c> onward, in order. Null entries mean "could not establish", which the
    /// caller must treat as a reason not to send. <see cref="TemplateVariableResult.Usage"/> is
    /// <see cref="AiUsage.None"/> when the model was not asked (everything typed, or nothing to fill).
    /// </summary>
    public async Task<TemplateVariableResult> ResolveAsync(
        Template template,
        AiBusinessContext business,
        IReadOnlyList<string> overrides,
        CancellationToken ct = default)
    {
        var needed = TemplateFill.VariableCount(template) - 1;      // {{1}} is always the first name
        if (needed <= 0) return new([], AiUsage.None);

        var hints = StarterTemplates.HintsFor(template.Name);
        var resolved = new string?[needed];
        var missing = new List<int>();

        for (var i = 0; i < needed; i++)
        {
            var typed = TemplateFill.Sanitize(overrides.ElementAtOrDefault(i));
            if (typed is not null) resolved[i] = typed;
            else missing.Add(i);
        }

        if (missing.Count == 0) return new(resolved, AiUsage.None);

        var asked = await planner.AskAsync(
            BuildSystem(business), BuildQuestion(template, hints, missing), MaxTokens, ct);
        var answer = asked.Text;

        if (answer is null)
        {
            logger.LogWarning(
                "Could not fill {Count} variable(s) for template {Name}: the model was unreachable.",
                missing.Count, template.Name);

            return new(resolved, asked.Usage);
        }

        var values = ParseValues(answer);

        foreach (var (index, position) in missing.Select((index, position) => (index, position)))
        {
            // The model is asked to answer with one value per line, in the order requested.
            resolved[index] = TemplateFill.Sanitize(values.ElementAtOrDefault(position));

            if (resolved[index] is null)
            {
                logger.LogWarning(
                    "Template {Name}: no usable value for variable {Variable}.", template.Name, index + 2);
            }
        }

        return new(resolved, asked.Usage);
    }

    private static string BuildSystem(AiBusinessContext business)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You fill in the blanks of a pre-approved WhatsApp message for a business.");
        sb.AppendLine();
        sb.AppendLine("RULES YOU MUST NOT BREAK");
        sb.AppendLine("- Answer ONLY from the business details below. Never invent a price, a date, a name");
        sb.AppendLine("  or an offer. If the details do not say, answer that line with exactly: UNKNOWN");
        sb.AppendLine("- One value per line, in the order asked, and nothing else. No numbering, no labels,");
        sb.AppendLine("  no quotes, no explanation.");
        sb.AppendLine("- Each value is a few words at most, on a single line.");
        sb.AppendLine("- The message is already written and approved; you are only supplying the values, so a");
        sb.AppendLine("  wrong one goes out looking official. UNKNOWN is always better than a guess.");
        sb.AppendLine();

        sb.AppendLine("THE BUSINESS");
        sb.AppendLine(business.BusinessName);
        sb.AppendLine(business.BusinessDescription);
        sb.AppendLine();

        if (business.Services.Count > 0)
        {
            sb.AppendLine("SERVICES");
            foreach (var s in business.Services) sb.Append("- ").AppendLine(s.Name);
            sb.AppendLine();
        }

        if (business.Faqs.Count > 0)
        {
            sb.AppendLine("PREPARED ANSWERS");
            foreach (var f in business.Faqs)
            {
                sb.Append("Q: ").AppendLine(f.Question.Trim());
                sb.Append("A: ").AppendLine(f.Answer.Trim());
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildQuestion(Template template, IReadOnlyList<string> hints, List<int> missing)
    {
        var sb = new StringBuilder();

        sb.AppendLine("This is the approved message:");
        sb.AppendLine(template.Components.Body.Text);
        sb.AppendLine();
        sb.AppendLine("Give me these values, one per line, in this order:");

        foreach (var index in missing)
        {
            var variable = index + 2;                                // {{1}} is the customer's name
            var hint = hints.ElementAtOrDefault(variable - 1);

            sb.Append("{{").Append(variable).Append("}} — ");
            sb.AppendLine(string.IsNullOrWhiteSpace(hint) ? "what belongs here" : hint);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// One value per line. Also tolerates a JSON array, because a model told to answer plainly sometimes
    /// answers in JSON anyway. UNKNOWN becomes null.
    /// </summary>
    private static List<string?> ParseValues(string answer)
    {
        var trimmed = answer.Trim();

        if (trimmed.StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string?>>(trimmed);
                if (parsed is not null) return [.. parsed.Select(Clean)];
            }
            catch (JsonException)
            {
                // Fall through to line parsing.
            }
        }

        return
        [
            .. trimmed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Clean),
        ];
    }

    private static string? Clean(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var value = line.Trim().Trim('"');
        return value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ? null : value;
    }
}

/// <summary>Resolved template values, plus what asking the model for them cost.</summary>
public sealed record TemplateVariableResult(IReadOnlyList<string?> Values, AiUsage Usage);
