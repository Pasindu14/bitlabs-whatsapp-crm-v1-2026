using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace wa_api.Infrastructure.Fx;

/// <summary>
/// Fetches USD→AED from a keyless public provider and caches it in-process.
/// <para>
/// Deliberately <see cref="IMemoryCache"/> rather than the shared <c>ICacheService</c>: Redis here runs
/// <c>--maxmemory-policy noeviction</c> because it backs Hangfire job state, so a cosmetic display rate
/// has no business competing for that budget — and this way a Redis outage can't affect price rendering.
/// One scalar per process is cheap to re-fetch after a restart.
/// </para>
/// </summary>
public sealed class FxRateService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<FxRateService> logger) : IFxRateService
{
    public const string HttpClientName = "fx";

    /// <summary>
    /// The dirham has been hard-pegged to the dollar at 3.6725 since 1997, so this is the correct
    /// answer rather than a degraded one — the live fetch exists to prove the peg still holds, not
    /// because the number is expected to move.
    /// </summary>
    public const decimal PeggedUsdAed = 3.6725m;

    private const string CacheKey = "fx:usd-aed";

    /// <summary>Live rates hold for half a day; the provider itself only republishes daily.</summary>
    private static readonly TimeSpan LiveTtl = TimeSpan.FromHours(12);

    /// <summary>Short TTL after a fallback so a transient provider outage self-heals quickly.</summary>
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(10);

    // A response outside this band means we grabbed the wrong field or an inverted quote — the peg is
    // safer than rendering a nonsense price to a customer.
    private const decimal MinPlausible = 3.0m;
    private const decimal MaxPlausible = 4.5m;

    public async Task<FxRate> GetUsdToAedAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out FxRate? cached) && cached is not null)
            return cached;

        var rate = await FetchAsync(ct);
        cache.Set(CacheKey, rate, rate.Source == "live" ? LiveTtl : FallbackTtl);
        return rate;
    }

    private async Task<FxRate> FetchAsync(CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("v6/latest/USD", ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("rates", out var rates)
                && rates.TryGetProperty("AED", out var aed)
                && aed.TryGetDecimal(out var value)
                && value is >= MinPlausible and <= MaxPlausible)
            {
                return new FxRate("USD", "AED", decimal.Round(value, 4), DateTime.UtcNow, "live");
            }

            logger.LogWarning("FX provider returned no plausible USD→AED rate; using the pegged rate.");
        }
        // A caller-initiated cancellation is not a provider failure — let it propagate. An HttpClient
        // timeout also surfaces as OperationCanceledException, hence the guard rather than a bare catch.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FX provider unreachable; using the pegged USD→AED rate.");
        }

        return new FxRate("USD", "AED", PeggedUsdAed, DateTime.UtcNow, "pegged");
    }
}
