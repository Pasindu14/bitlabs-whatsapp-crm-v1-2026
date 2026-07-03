using wa_api.Features.Contacts;
using Xunit;

namespace wa_api.Tests.Contacts;

/// <summary>Pins the E.164 normalization rules used at contact create/update and bulk import.</summary>
public class PhoneNumberTests
{
    [Theory]
    [InlineData("971501234567", "971501234567")]      // already clean
    [InlineData("+971 50 123 4567", "971501234567")]  // strips + and spaces
    [InlineData("+94 (77) 123-4567", "94771234567")]  // strips punctuation
    [InlineData("0094771234567", "94771234567")]      // strips international 00 prefix
    public void TryNormalize_ValidNumbers_AreNormalized(string raw, string expected)
    {
        Assert.True(PhoneNumber.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]                 // empty
    [InlineData("   ")]              // whitespace
    [InlineData("1234567")]          // 7 digits — too short
    [InlineData("1234567890123456")] // 16 digits — too long
    [InlineData("0771234567")]       // national format, leading 0 (no country code)
    [InlineData("abcdefgh")]         // no digits
    public void TryNormalize_InvalidNumbers_AreRejected(string raw)
    {
        Assert.False(PhoneNumber.TryNormalize(raw, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
