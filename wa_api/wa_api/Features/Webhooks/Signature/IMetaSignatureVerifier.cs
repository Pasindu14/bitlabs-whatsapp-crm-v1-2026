namespace wa_api.Features.Webhooks.Signature;

public interface IMetaSignatureVerifier
{
    /// <summary>
    /// Verifies using a caller-supplied <paramref name="secret"/> (per-connection App Secret).
    /// Preferred overload for multi-tenant use where each company has its own Meta App.
    /// </summary>
    bool Verify(string rawBody, string? signatureHeader, string secret);

    /// <summary>
    /// Fallback: verifies using the global <c>Meta:AppSecret</c> from configuration.
    /// </summary>
    bool Verify(string rawBody, string? signatureHeader);
}
