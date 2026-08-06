namespace wa_api.Infrastructure.PayHere;

public sealed class PayHereOptions
{
    /// <summary>PayHere Merchant ID for the account (sandbox or live per <see cref="Sandbox"/>).</summary>
    public string MerchantId { get; set; } = null!;

    /// <summary>PayHere Merchant Secret — used to compute/verify the MD5 hash. Never sent to the client.</summary>
    public string MerchantSecret { get; set; } = null!;

    /// <summary>
    /// True to use PayHere's sandbox environment (sandbox.payhere.lk). The same payhere.js script
    /// serves both; this flag is passed through to the frontend payment object which PayHere reads.
    /// </summary>
    public bool Sandbox { get; set; } = true;

    /// <summary>
    /// Server-to-server callback PayHere POSTs the payment outcome to. Must be a publicly
    /// reachable HTTPS URL — PayHere cannot reach localhost.
    /// </summary>
    public string NotifyUrl { get; set; } = null!;

    /// <summary>Browser redirect target on success (fallback if the onsite popup can't complete inline).</summary>
    public string ReturnUrl { get; set; } = null!;

    /// <summary>Browser redirect target on cancel (fallback if the onsite popup can't complete inline).</summary>
    public string CancelUrl { get; set; } = null!;
}
