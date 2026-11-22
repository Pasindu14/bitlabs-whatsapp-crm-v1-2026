using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace wa_api.Features.Messages;

/// <summary>USD per delivered message for one market, keyed by Meta's pricing category.</summary>
public sealed class MetaCategoryRates
{
    public decimal Marketing { get; set; }
    public decimal Utility { get; set; }
    public decimal Authentication { get; set; }

    /// <summary>
    /// Non-template replies inside the customer service window. Free until 30 Sep 2026; from 1 Oct 2026
    /// Meta bills them at the market's utility rate.
    /// </summary>
    public decimal Service { get; set; }
}

/// <summary>
/// Meta's rate card, bound from <c>Meta:Pricing</c>. Meta charges by the RECIPIENT's country, not the
/// business's — a UAE clinic messaging a UK number pays the UK rate — so markets are keyed by calling
/// code (<c>"971"</c>, <c>"44"</c>, <c>"1876"</c>…) and matched longest-prefix first.
/// </summary>
public sealed class MetaPricingOptions
{
    public const string SectionName = "Meta:Pricing";

    public Dictionary<string, MetaCategoryRates> Countries { get; set; } = new();

    /// <summary>Used for any number whose calling code is not listed.</summary>
    public MetaCategoryRates Default { get; set; } = new();
}

public interface IMetaCostCalculator
{
    /// <summary>
    /// Estimated USD for one message. 0 when Meta said it was not billable (free tier, free entry point);
    /// null when billability is not known yet.
    /// </summary>
    decimal? CostUsd(string? recipientPhone, string? category, bool? billable);
}

/// <summary>
/// Prices a delivered message from the configured rate card. Meta's own <c>billable</c> flag decides
/// WHETHER it was charged — including the 1,000 free service messages per number per month — so that
/// allowance is never re-implemented here; this only decides HOW MUCH.
/// </summary>
public sealed class MetaCostCalculator(IOptions<MetaPricingOptions> options, ILogger<MetaCostCalculator> logger)
    : IMetaCostCalculator
{
    private readonly ConcurrentDictionary<string, bool> _warned = new(StringComparer.OrdinalIgnoreCase);

    public decimal? CostUsd(string? recipientPhone, string? category, bool? billable)
    {
        if (billable is null) return null;
        if (billable == false) return 0m;

        var rates = RatesFor(recipientPhone);

        switch (category?.Trim().ToLowerInvariant())
        {
            case "marketing":
            case "marketing_lite":
                return rates.Marketing;
            case "utility":
                return rates.Utility;
            case "authentication":
            case "authentication_international":
                return rates.Authentication;
            case "service":
                return rates.Service;
            default:
                // Billed but in a category we have no rate for: price it as utility (the rate service
                // messages use) and say so once, rather than dropping real spend from the report.
                if (_warned.TryAdd(category ?? "(none)", true))
                    logger.LogWarning(
                        "Billable Meta message with unknown pricing category {Category}; priced at the utility rate.",
                        category ?? "(none)");
                return rates.Utility;
        }
    }

    private MetaCategoryRates RatesFor(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var countries = options.Value.Countries;

        // Calling codes are 1–4 digits (with NANP area codes like 1876 listed on their own). Longest wins.
        for (var length = Math.Min(4, digits.Length); length >= 1; length--)
        {
            if (countries.TryGetValue(digits[..length], out var rates)) return rates;
        }

        return options.Value.Default;
    }
}
