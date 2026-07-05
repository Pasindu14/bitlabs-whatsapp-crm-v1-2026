namespace wa_api.Features.Billing;

/// <summary>
/// Converts Stripe's integer amounts (smallest currency unit) to a decimal major-unit amount for display.
/// Stripe stores most currencies in cents (÷100), but <b>zero-decimal</b> currencies (JPY, KRW, …) are
/// already whole units — dividing those by 100 misreports the total 100× too small. Three-decimal currencies
/// are billed by Stripe in a cents-equivalent and displayed ÷100, so they take the default path.
/// </summary>
public static class StripeCurrency
{
    // Stripe's zero-decimal currency set (https://stripe.com/docs/currencies#zero-decimal).
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga",
        "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf",
    };

    /// <summary>Major-unit amount for display: the smallest-unit <paramref name="amount"/> divided by the
    /// currency's unit factor (1 for zero-decimal currencies, 100 otherwise).</summary>
    public static decimal ToMajorUnit(long amount, string? currency) =>
        currency is not null && ZeroDecimal.Contains(currency) ? amount : amount / 100m;
}
