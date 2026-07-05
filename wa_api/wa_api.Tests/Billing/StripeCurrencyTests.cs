using wa_api.Features.Billing;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins zero-decimal currency handling for invoice display (M16). Stripe stores most currencies in cents
/// (÷100), but zero-decimal currencies (JPY, KRW, …) are already whole units — dividing those by 100 reported
/// the total 100× too small.
/// </summary>
public class StripeCurrencyTests
{
    [Theory]
    [InlineData(1999, "usd", 19.99)]   // cents → dollars
    [InlineData(1999, "eur", 19.99)]
    [InlineData(5000, "gbp", 50.00)]
    public void TwoDecimalCurrency_DividesBy100(long amount, string currency, decimal expected)
        => Assert.Equal(expected, StripeCurrency.ToMajorUnit(amount, currency));

    [Theory]
    [InlineData(5000, "jpy", 5000)]    // yen are whole units — NOT 50
    [InlineData(1500, "krw", 1500)]
    [InlineData(1000, "vnd", 1000)]
    [InlineData(200, "xaf", 200)]
    public void ZeroDecimalCurrency_IsUnscaled(long amount, string currency, decimal expected)
        => Assert.Equal(expected, StripeCurrency.ToMajorUnit(amount, currency));

    [Fact]
    public void CurrencyCode_IsCaseInsensitive()
        => Assert.Equal(5000m, StripeCurrency.ToMajorUnit(5000, "JPY"));

    [Fact]
    public void NullCurrency_FallsBackToDivideBy100()
        => Assert.Equal(19.99m, StripeCurrency.ToMajorUnit(1999, null));
}
