using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace wa_api.Features.Bot.Ai;

/// <summary>USD per million tokens for one model, bound from <c>Anthropic:Pricing:{model}</c>.</summary>
public sealed class AnthropicModelPrice
{
    public decimal Input { get; set; }
    public decimal Output { get; set; }

    /// <summary>5-minute cache write — the only TTL this app uses.</summary>
    public decimal CacheWrite5m { get; set; }

    public decimal CacheRead { get; set; }
}

public interface IAiCostCalculator
{
    /// <summary>
    /// USD for the given usage, or null when the model has no price configured. Null rather than zero,
    /// so a report can tell "free" from "unknown".
    /// </summary>
    decimal? CostUsd(string model, AiUsage usage);
}

/// <summary>
/// Prices tokens from config rather than code, so a price change is a settings edit. The cost is
/// stored on the log row at write time, so changing a price never rewrites history.
/// </summary>
public sealed class AiCostCalculator(IOptions<AnthropicOptions> options, ILogger<AiCostCalculator> logger)
    : IAiCostCalculator
{
    private const decimal PerMillion = 1_000_000m;

    // One warning per unpriced model per process, not one per reply.
    private readonly ConcurrentDictionary<string, bool> _warned = new(StringComparer.OrdinalIgnoreCase);

    public decimal? CostUsd(string model, AiUsage usage)
    {
        var prices = options.Value.Pricing;
        var price = prices.FirstOrDefault(p => string.Equals(p.Key, model, StringComparison.OrdinalIgnoreCase)).Value;

        if (price is null)
        {
            if (_warned.TryAdd(model, true))
                logger.LogWarning(
                    "No price configured for model {Model} under Anthropic:Pricing — its calls are logged "
                    + "without a cost.", model);
            return null;
        }

        return Compute(price, usage);
    }

    public static decimal Compute(AnthropicModelPrice price, AiUsage usage) => Math.Round(
        (usage.InputTokens * price.Input
         + usage.OutputTokens * price.Output
         + usage.CacheWriteTokens * price.CacheWrite5m
         + usage.CacheReadTokens * price.CacheRead) / PerMillion,
        6);
}
