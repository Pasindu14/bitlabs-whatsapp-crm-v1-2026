using System.Net.Http.Headers;
using System.Text.Json;
using wa_api.Common.Errors;

namespace wa_api.Features.WhatsApp;

public interface IMetaCredentialValidator
{
    /// <summary>
    /// Calls the Meta Graph API to confirm — in a single request to <c>/{wabaId}/phone_numbers</c> —
    /// that (1) the WhatsApp Business Account id exists, (2) the access token has permission to read it,
    /// and (3) the phone-number id actually belongs to that WABA.
    /// <para>
    /// Throws <see cref="BusinessRuleException"/> with code <c>WABA_TOKEN_INVALID</c> when Meta cannot
    /// load the WABA with this token, or <c>WABA_PHONE_NUMBER_MISMATCH</c> when the WABA is readable but
    /// does not own the given phone-number id. Throws <see cref="InfrastructureException"/> on network failure.
    /// </para>
    /// </summary>
    Task ValidateAsync(string phoneNumberId, string wabaId, string accessToken, CancellationToken ct = default);
}

public class MetaCredentialValidator(IHttpClientFactory httpClientFactory) : IMetaCredentialValidator
{
    private const string MetaApiVersion = "v19.0";

    public async Task ValidateAsync(string phoneNumberId, string wabaId, string accessToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");

        // List the phone numbers Meta reports under this WABA. One call proves three things at once:
        //   1. the WABA id exists,
        //   2. the token has permission to read it, and
        //   3. (membership check below) the phone-number id actually belongs to it.
        // A bare GET /{phoneNumberId} validated the phone+token but never the WABA id — which is how a
        // mistyped WABA id used to pass save and only fail later at template-submit time.
        var url = $"{MetaApiVersion}/{wabaId}/phone_numbers?fields=id&limit=100";
        var found = false;

        try
        {
            // Follow paging in case a WABA has many numbers (rare, but cheap and bounded to stay safe).
            for (var page = 0; page < 20 && url is not null && !found; page++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    var message = ParseMetaErrorMessage(body) ?? $"Meta returned HTTP {(int)resp.StatusCode}";
                    throw new BusinessRuleException("WABA_TOKEN_INVALID",
                        $"WhatsApp Business Account ID or access token is invalid: {message}");
                }

                (found, url) = ScanPhoneNumbers(body, phoneNumberId);
            }
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

        if (!found)
            throw new BusinessRuleException("WABA_PHONE_NUMBER_MISMATCH",
                $"Phone number id '{phoneNumberId}' does not belong to WhatsApp Business Account '{wabaId}'. " +
                "Make sure the phone number id and the WABA id are from the same account.");
    }

    /// <summary>
    /// Scans one page of the <c>/phone_numbers</c> response for <paramref name="phoneNumberId"/> and
    /// returns whether it was found plus the absolute URL of the next page (null when there is none).
    /// </summary>
    private static (bool found, string? next) ScanPhoneNumbers(string body, string phoneNumberId)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.GetString() == phoneNumberId)
                        return (true, null);
                }
            }

            // Graph "next" is an absolute URL (already carries the access token) — pass it through verbatim.
            if (root.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("next", out var next))
                return (false, next.GetString());
        }
        catch { /* unexpected shape — treat as no match, no next page */ }

        return (false, null);
    }

    private static string? ParseMetaErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch { /* not JSON or unexpected shape */ }
        return null;
    }
}
