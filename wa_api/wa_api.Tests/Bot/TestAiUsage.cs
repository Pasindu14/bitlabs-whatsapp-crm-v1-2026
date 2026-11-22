using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Features.Bot.Ai;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Tests.Bot;

/// <summary>The real usage logger and cost calculator, priced at Haiku 4.5's published rates.</summary>
internal static class TestAiUsage
{
    public static AnthropicOptions Options() => new()
    {
        Pricing = new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-4-5"] = new AnthropicModelPrice
            {
                Input = 1.00m, Output = 5.00m, CacheWrite5m = 1.25m, CacheRead = 0.10m,
            },
        },
    };

    public static AiCostCalculator Calculator() =>
        new(Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<AiCostCalculator>.Instance);

    public static AiUsageLogger Logger(AppDbContext db) => new(db, Calculator());
}
