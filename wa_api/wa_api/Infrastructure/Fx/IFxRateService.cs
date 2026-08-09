namespace wa_api.Infrastructure.Fx;

/// <summary>
/// Supplies the USD→AED reference rate used to <b>display</b> an indicative dirham price next to a
/// USD plan price. This never touches what a customer is charged — PayHere cannot process AED at
/// all (LKR/USD/GBP/EUR/AUD only), so every order stays USD-denominated.
/// </summary>
public interface IFxRateService
{
    Task<FxRate> GetUsdToAedAsync(CancellationToken ct = default);
}

/// <param name="Rate">Units of <paramref name="Quote"/> per 1 <paramref name="Base"/>.</param>
/// <param name="Source">
/// <c>"live"</c> when the upstream provider answered with a plausible rate, <c>"pegged"</c> when the
/// hard-coded UAE peg was substituted because the provider failed. Exposed so a stuck rate is
/// visible to the client rather than silently indistinguishable from a fresh one.
/// </param>
public sealed record FxRate(string Base, string Quote, decimal Rate, DateTime FetchedAt, string Source);
