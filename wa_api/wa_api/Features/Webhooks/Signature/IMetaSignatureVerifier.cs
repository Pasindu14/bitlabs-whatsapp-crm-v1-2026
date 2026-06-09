namespace wa_api.Features.Webhooks.Signature;

public interface IMetaSignatureVerifier
{
    /// <summary>
    /// True when <paramref name="signatureHeader"/> (the <c>X-Hub-Signature-256</c> value) matches
    /// <c>sha256=</c> + HMAC-SHA256(<paramref name="rawBody"/>, <c>Meta:AppSecret</c>). Fails closed.
    /// </summary>
    bool Verify(string rawBody, string? signatureHeader);
}
