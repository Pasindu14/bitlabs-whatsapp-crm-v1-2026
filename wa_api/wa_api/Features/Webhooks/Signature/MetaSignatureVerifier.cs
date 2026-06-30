using System.Security.Cryptography;
using System.Text;

namespace wa_api.Features.Webhooks.Signature;

/// <summary>
/// Verifies Meta's <c>X-Hub-Signature-256</c> header — <c>sha256=</c> + HMAC-SHA256(rawBody, App Secret).
/// The HMAC must run over the EXACT bytes Meta sent, so the controller passes the raw request body (never
/// a re-serialization). Fails closed when the secret is unconfigured or the header is missing/malformed.
/// </summary>
public sealed class MetaSignatureVerifier(IConfiguration config, ILogger<MetaSignatureVerifier> logger)
    : IMetaSignatureVerifier
{
    private const string Prefix = "sha256=";

    public bool Verify(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;

        if (string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var providedHex = signatureHeader[Prefix.Length..].Trim().ToLowerInvariant();
        var computedHex = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

        var computed = Encoding.ASCII.GetBytes(computedHex);
        var provided = Encoding.ASCII.GetBytes(providedHex);
        return computed.Length == provided.Length && CryptographicOperations.FixedTimeEquals(computed, provided);
    }

    public bool Verify(string rawBody, string? signatureHeader)
    {
        var secret = config["Meta:AppSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("Meta:AppSecret is not configured — rejecting webhook (fail closed).");
            return false;
        }
        return Verify(rawBody, signatureHeader, secret);
    }
}
