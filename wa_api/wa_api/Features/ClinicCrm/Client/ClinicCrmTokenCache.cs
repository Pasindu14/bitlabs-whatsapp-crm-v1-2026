using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace wa_api.Features.ClinicCrm.Client;

/// <summary>
/// Access tokens, one per set of credentials, shared across requests and jobs in this process.
/// <para>
/// The CRM allows 10 token requests a minute and a token lives 15 minutes, so asking for one per message
/// would hit the limit on a busy afternoon. The cache key includes the credentials themselves (hashed), so
/// an admin saving a new secret naturally starts a new token instead of reusing one for the old secret.
/// </para>
/// </summary>
public sealed class ClinicCrmTokenCache
{
    private sealed record Entry(string Token, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, Entry> _tokens = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static string KeyFor(ClinicCrmEndpoint endpoint)
    {
        var material = $"{endpoint.BaseUrl}\n{endpoint.ClientId}\n{endpoint.ClientSecret}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return $"{endpoint.ConnectionId:N}:{hash}";
    }

    /// <summary>A token still valid for at least <paramref name="margin"/>.</summary>
    public bool TryGet(string key, TimeSpan margin, out string token)
    {
        if (_tokens.TryGetValue(key, out var entry) && entry.ExpiresAtUtc - margin > DateTime.UtcNow)
        {
            token = entry.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    public void Set(string key, string token, DateTime expiresAtUtc) => _tokens[key] = new Entry(token, expiresAtUtc);

    /// <summary>Drops a token the CRM refused, so the next call fetches a new one.</summary>
    public void Invalidate(string key, string token)
    {
        // Only the token that failed: another request may already have replaced it with a good one.
        if (_tokens.TryGetValue(key, out var entry) && entry.Token == token)
            _tokens.TryRemove(key, out _);
    }

    /// <summary>One token request at a time per key — concurrent replies wait for the first one's token.</summary>
    public SemaphoreSlim LockFor(string key) => _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
}
