using System.Security.Cryptography;
using System.Text;

namespace wa_api.Infrastructure.Security;

/// <summary>
/// Encrypts/decrypts sensitive secrets (WABA access tokens, per-connection app secrets) at the persistence
/// boundary. Applied transparently by an EF value converter, so callers keep working with plaintext.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Encrypt a plaintext secret for storage. Empty or already-encrypted input passes through.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypt a stored secret. Legacy PLAINTEXT (no version prefix) is returned unchanged, which
    /// makes rollout non-breaking — existing rows keep working until they're next written / backfilled.</summary>
    string Unprotect(string stored);
}

/// <summary>
/// Passthrough protector used when no encryption key is configured (dev, or before a key is provisioned in
/// prod). Keeps the app fully functional — secrets are simply stored as-is, exactly as before H3.
/// </summary>
public sealed class NullTokenProtector : ITokenProtector
{
    public static readonly NullTokenProtector Instance = new();
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string stored) => stored;
}

/// <summary>
/// AES-256-GCM at-rest protection. Ciphertext carries a version prefix so <see cref="Unprotect"/> can pass
/// legacy plaintext through untouched. Format: <c>enc:v1:</c> + base64(nonce(12) ‖ ciphertext ‖ tag(16)).
/// A fresh random nonce per call means the same plaintext encrypts to different ciphertext each time.
/// </summary>
public sealed class AesGcmTokenProtector : ITokenProtector
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12;   // AES-GCM standard nonce length
    private const int TagSize = 16;
    private readonly byte[] _key;       // 32 bytes (AES-256)

    public AesGcmTokenProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Encryption key must be exactly 32 bytes (AES-256).", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        // Empty stays empty (the column's "has a token?" checks compare against ""); never double-encrypt.
        if (string.IsNullOrEmpty(plaintext) || plaintext.StartsWith(Prefix, StringComparison.Ordinal))
            return plaintext;

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSize + cipher.Length, TagSize);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;   // legacy plaintext or empty — pass through

        var blob = Convert.FromBase64String(stored[Prefix.Length..]);
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(blob.Length - TagSize, TagSize);
        var cipher = blob.AsSpan(NonceSize, blob.Length - NonceSize - TagSize);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
