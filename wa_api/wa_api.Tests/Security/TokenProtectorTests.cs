using System.Security.Cryptography;
using wa_api.Infrastructure.Security;
using Xunit;

namespace wa_api.Tests.Security;

/// <summary>
/// Pins the at-rest secret protector (H3). Round-trips through AES-256-GCM, is non-breaking on legacy
/// plaintext, keeps empty empty (so "has a token?" column checks still work), and never double-encrypts.
/// </summary>
public class TokenProtectorTests
{
    private static AesGcmTokenProtector NewProtector() =>
        new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void RoundTrips_Plaintext()
    {
        var p = NewProtector();
        const string token = "EAAG...a-real-looking-meta-token_1234567890";

        var stored = p.Protect(token);
        Assert.StartsWith(AesGcmTokenProtector.Prefix, stored);
        Assert.NotEqual(token, stored);
        Assert.Equal(token, p.Unprotect(stored));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_PassesThrough()
    {
        // A pre-encryption row read back must return unchanged — this is what makes rollout non-breaking.
        var p = NewProtector();
        const string legacy = "plaintext-token-no-prefix";
        Assert.Equal(legacy, p.Unprotect(legacy));
    }

    [Fact]
    public void Empty_StaysEmpty_BothWays()
    {
        var p = NewProtector();
        Assert.Equal("", p.Protect(""));
        Assert.Equal("", p.Unprotect(""));
    }

    [Fact]
    public void Protect_IsNotDoubleApplied()
    {
        var p = NewProtector();
        var once = p.Protect("secret");
        var twice = p.Protect(once);   // already enc:v1: — must be left as-is
        Assert.Equal(once, twice);
        Assert.Equal("secret", p.Unprotect(twice));
    }

    [Fact]
    public void FreshNoncePerCall_ProducesDifferentCiphertext()
    {
        var p = NewProtector();
        Assert.NotEqual(p.Protect("same"), p.Protect("same"));
    }

    [Fact]
    public void NullProtector_IsPurePassthrough()
    {
        var p = NullTokenProtector.Instance;
        Assert.Equal("x", p.Protect("x"));
        Assert.Equal("x", p.Unprotect("x"));
    }

    [Fact]
    public void WrongKey_FailsToDecrypt()
    {
        var stored = NewProtector().Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(stored));
    }

    [Fact]
    public void Constructor_RejectsNon32ByteKey()
        => Assert.Throws<ArgumentException>(() => new AesGcmTokenProtector(RandomNumberGenerator.GetBytes(16)));
}
