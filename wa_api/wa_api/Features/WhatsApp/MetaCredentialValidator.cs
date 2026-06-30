using System.Net.Http.Headers;
using System.Text.Json;
using wa_api.Common.Errors;

namespace wa_api.Features.WhatsApp;

public interface IMetaCredentialValidator
{
    /// <summary>
    /// Calls the Meta Graph API to confirm the token can read the given phone-number id.
    /// Throws <see cref="BusinessRuleException"/> with code <c>WABA_TOKEN_INVALID</c> when
    /// Meta rejects the credential. Throws <see cref="InfrastructureException"/> on network failure.
    /// </summary>
    Task ValidateAsync(string phoneNumberId, string accessToken, CancellationToken ct = default);
}

public class MetaCredentialValidator(IHttpClientFactory httpClientFactory) : IMetaCredentialValidator
{
    private const string MetaApiVersion = "v19.0";

    public async Task ValidateAsync(string phoneNumberId, string accessToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");
        var url = $"{MetaApiVersion}/{phoneNumberId}?fields=id";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await client.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
                return;

            var body = await resp.Content.ReadAsStringAsync(ct);
            var message = ParseMetaErrorMessage(body) ?? $"Meta returned HTTP {(int)resp.StatusCode}";
            throw new BusinessRuleException("WABA_TOKEN_INVALID", message);
        }
        catch (BusinessRuleException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InfrastructureException("META_API_UNAVAILABLE",
                $"Could not reach Meta Graph API to validate credentials: {ex.Message}");
        }
    }

    private static string? ParseMetaErrorMessage(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch { /* not JSON or unexpected shape */ }
        return null;
    }
}
