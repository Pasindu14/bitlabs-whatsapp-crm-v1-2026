using wa_api.Features.Calendar;
using Xunit;

namespace wa_api.Tests.Calendar;

/// <summary>
/// Pins the timezone plumbing the calendar depends on — in particular that a local day boundary can
/// always be converted, including in the zones whose clocks shift at midnight.
/// </summary>
public class CompanyTimeZoneTests
{
    [Theory]
    [InlineData("Asia/Colombo")]
    [InlineData("Europe/London")]
    [InlineData("UTC")]
    public void ResolvesKnownZones(string id) => Assert.True(CompanyTimeZone.IsValid(id));

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsUnknownZones(string? id) => Assert.False(CompanyTimeZone.IsValid(id));

    [Fact]
    public void FallsBackToUtcRatherThanThrowingOnAnUnknownZone()
    {
        Assert.Equal(TimeZoneInfo.Utc, CompanyTimeZone.ResolveOrFallback("Mars/Olympus"));
    }

    [Fact]
    public void TreatsAKindlessTimestampAsAlreadyUtc()
    {
        // Naive JSON (no trailing Z) must not be shifted by the server's own offset, and Npgsql
        // rejects a non-UTC kind for a timestamptz column.
        var naive = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var result = CompanyTimeZone.AsUtc(naive);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void DayBoundaryConvertsAnOrdinaryLocalMidnight()
    {
        var colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

        // Colombo is UTC+05:30 year-round, so local midnight is 18:30Z the previous day.
        Assert.Equal(
            new DateTime(2026, 5, 31, 18, 30, 0, DateTimeKind.Utc),
            CompanyTimeZone.DayBoundaryToUtc(new DateOnly(2026, 6, 1), colombo));
    }

    [Fact]
    public void DayBoundarySurvivesAZoneWhoseClocksShiftAtMidnight()
    {
        // America/Santiago springs forward AT midnight, so 00:00 does not exist on the transition
        // date. TimeZoneInfo.ConvertTimeToUtc throws on exactly this input; the availability query
        // would 500 a few times a year in such a zone if the boundary were converted naively.
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        var boundary = CompanyTimeZone.DayBoundaryToUtc(new DateOnly(2026, 9, 6), santiago);

        Assert.Equal(DateTimeKind.Utc, boundary.Kind);
        // Only the absence of a throw, and a sane instant, are contractual here — a range boundary
        // landing an hour either side of the transition is harmless.
        Assert.InRange(
            boundary,
            new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 6, 6, 0, 0, DateTimeKind.Utc));
    }
}
