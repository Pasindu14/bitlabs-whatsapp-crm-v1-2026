namespace wa_api.Features.Messages;

/// <summary>
/// Meta Cloud API error codes that indicate policy violations or account health issues.
/// When these codes are returned, the correct response is to stop sending — not retry.
/// See: https://developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes
/// </summary>
public static class MetaPolicyErrorCodes
{
    /// <summary>Spam rate limit — message rejected because the number is sending spam. Pause campaign.</summary>
    public const string SpamRateLimit = "131048";

    /// <summary>Healthy-ecosystem limit — sending volume is harming the broader ecosystem. Pause campaign.</summary>
    public const string HealthyEcosystemLimit = "131049";

    /// <summary>Account restricted — Meta has placed a restriction on this account. Stop all sends.</summary>
    public const string AccountRestricted = "368";

    /// <summary>Account suspended — Meta has suspended this account. Stop all sends immediately.</summary>
    public const string AccountSuspended = "131031";

    /// <summary>Throughput rate limit — too many messages too fast. Transient: back off and retry, do NOT fail.</summary>
    public const string RateLimited = "130429";

    /// <summary>Per-recipient pair rate limit — too many messages to one number. Transient: back off and retry.</summary>
    public const string PairRateLimit = "131056";

    /// <summary>
    /// Message undeliverable — the recipient's number is not a WhatsApp user (or cannot receive messages).
    /// The Cloud API has no pre-send registration lookup, so this is how we learn a number isn't on WhatsApp.
    /// Permanent for that contact: stamp it invalid and skip it in future sends — never retry.
    /// </summary>
    public const string Undeliverable = "131026";

    /// <summary>Access token expired or invalid (190). Fatal for the whole connection — halt, don't per-recipient fail.</summary>
    public const string AccessTokenInvalid = "190";

    /// <summary>
    /// Returns true when the error code is a transient throughput throttle (Meta asking us to slow down).
    /// The send did not happen, so the recipient must stay Queued and the batch should back off and retry —
    /// treating these as permanent failures would silently drop deliverable messages.
    /// </summary>
    public static bool IsTransientThrottle(string? code) =>
        code is RateLimited or PairRateLimit;

    /// <summary>
    /// Returns true when the error code is a per-campaign pause signal (spam / ecosystem limit).
    /// The campaign should be paused and the tenant notified to review quality.
    /// </summary>
    public static bool IsCampaignPause(string? code) =>
        code is SpamRateLimit or HealthyEcosystemLimit;

    /// <summary>
    /// Returns true when the error code indicates an account-level restriction.
    /// All sends for this WABA must stop until the account is resolved in Meta Business Suite.
    /// </summary>
    public static bool IsAccountRestriction(string? code) =>
        code is AccountRestricted or AccountSuspended;

    /// <summary>
    /// Returns true when the error code means the recipient is not a reachable WhatsApp user.
    /// The contact should be stamped invalid so it is skipped in all future sends — this is the
    /// only signal the Cloud API gives us that a number isn't on WhatsApp.
    /// </summary>
    public static bool IsUndeliverableRecipient(string? code) =>
        code is Undeliverable;

    /// <summary>
    /// Returns true for an expired/invalid access token (190). This is fatal for the entire connection — every
    /// remaining send would fail identically — so the caller must halt the campaign and mark the connection
    /// invalid immediately, not fail the audience one recipient at a time.
    /// </summary>
    public static bool IsAuthError(string? code) =>
        code is AccessTokenInvalid;

    /// <summary>
    /// Returns true for a transient transport/server error (a network blip, a Meta 5xx / 408, or a bare HTTP
    /// 429 with no Meta policy code). The send did not land, so the recipient must stay Queued and the batch
    /// back off and retry — permanently failing these would drop deliverable messages over a temporary glitch.
    /// Distinct from the Meta policy throttle codes handled by <see cref="IsTransientThrottle"/>.
    /// </summary>
    public static bool IsTransientTransportError(string? code) =>
        code is "NETWORK_ERROR" or "HTTP_408" or "HTTP_429"
        || (code is not null && code.StartsWith("HTTP_5", StringComparison.Ordinal));
}
