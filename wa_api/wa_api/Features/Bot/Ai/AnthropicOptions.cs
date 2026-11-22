namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Bound from the <c>Anthropic</c> configuration section, exactly like <c>StripeOptions</c> and
/// <c>PayHereOptions</c>.
/// <para>
/// <see cref="ApiKey"/> belongs in user-secrets or an environment variable and NOWHERE else. This repo
/// commits <c>appsettings.Development.json</c>, so a key placed there would be published the moment it
/// was written.
/// </para>
/// <para>
/// An unset key is not a startup failure: the AI path skips itself and the keyword rules keep
/// answering, which is the behaviour that shipped before this engine existed. That makes the missing
/// key a kill switch rather than an outage.
/// </para>
/// </summary>
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>The named <see cref="HttpClient"/> the SDK's transport is configured from.</summary>
    public const string HttpClientName = "Anthropic";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Haiku 4.5 by default: this runs once per inbound customer message, so per-token cost and
    /// time-to-first-word matter more here than raw capability, and the hard parts of the job
    /// (availability, overlap, offerability) are enforced by the calendar module rather than the model.
    /// </summary>
    public string Model { get; set; } = "claude-haiku-4-5";

    /// <summary>
    /// Ceiling per reply. Deliberately small — a WhatsApp reply is two sentences, and a low ceiling
    /// makes a prompt-injected essay cheap rather than expensive.
    /// </summary>
    public int MaxTokens { get; set; } = 700;

    /// <summary>
    /// Per-request timeout. The customer is waiting, and the job's own Hangfire retry is the backstop
    /// for a slow upstream, so failing fast beats holding a worker.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// USD per million tokens, keyed by model id. Used only to put a cost on each log row — a missing
    /// entry never blocks a reply, it just leaves that row's cost empty.
    /// </summary>
    public Dictionary<string, AnthropicModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the engine has everything it needs to make a call.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
