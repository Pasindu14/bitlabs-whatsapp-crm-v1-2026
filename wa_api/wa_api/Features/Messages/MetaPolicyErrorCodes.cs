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
}
