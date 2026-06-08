using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa_api.Features.Templates;

/// <summary>Outcome of a Meta template create call.</summary>
public record MetaTemplateResult(string? MetaTemplateId, string? Status, string? Error);

/// <summary>Latest status of a template as reported by Meta (Plan 004 Step 3).</summary>
public record MetaTemplateStatus(string? Status, string? Category, string? RejectionReason);

/// <summary>
/// Thin typed wrapper over the shared <c>MetaGraph</c> HttpClient for the WhatsApp
/// message-template endpoints. Mirrors <c>MessageService.CallMetaApiAsync</c> (same client,
/// same <c>v19.0</c> version, bearer token per request).
/// </summary>
public interface IMetaTemplateClient
{
    /// <summary>POST /{wabaId}/message_templates — submit a template for approval.</summary>
    Task<MetaTemplateResult> CreateAsync(string wabaId, string accessToken, object payload, CancellationToken ct = default);

    /// <summary>GET /{metaTemplateId} — current status/category of a submitted template.</summary>
    Task<MetaTemplateStatus?> GetStatusAsync(string metaTemplateId, string accessToken, CancellationToken ct = default);

    /// <summary>DELETE /{wabaId}/message_templates?name= — remove a template from Meta. Returns true on success.</summary>
    Task<bool> DeleteAsync(string wabaId, string name, string accessToken, CancellationToken ct = default);
}

public class MetaTemplateClient(IHttpClientFactory httpClientFactory, ILogger<MetaTemplateClient> logger)
    : IMetaTemplateClient
{
    private const string ApiVersion = "v19.0";

    // No naming policy: we build exact Meta keys (snake_case + UPPERCASE values) ourselves.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MetaTemplateResult> CreateAsync(
        string wabaId, string accessToken, object payload, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiVersion}/{wabaId}/message_templates") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
                return new MetaTemplateResult(id, status, null);
            }

            var error = MetaError.Extract(json) ?? $"Meta API error {(int)res.StatusCode}";
            logger.LogWarning("Meta template create returned {Status}: {Error}", (int)res.StatusCode, error);
            return new MetaTemplateResult(null, null, error);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Meta template create");
            return new MetaTemplateResult(null, null, "Could not reach WhatsApp API. Please try again.");
        }
    }

    public async Task<MetaTemplateStatus?> GetStatusAsync(
        string metaTemplateId, string accessToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiVersion}/{metaTemplateId}?fields=status,category,rejected_reason");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Meta template status {Status}: {Body}", (int)res.StatusCode, MetaError.Extract(json));
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new MetaTemplateStatus(
                root.TryGetProperty("status", out var s) ? s.GetString() : null,
                root.TryGetProperty("category", out var c) ? c.GetString() : null,
                root.TryGetProperty("rejected_reason", out var r) ? r.GetString() : null);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error polling Meta template status");
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string wabaId, string name, string accessToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");
        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"{ApiVersion}/{wabaId}/message_templates?name={Uri.EscapeDataString(name)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Meta template delete {Status}: {Error}", (int)res.StatusCode, MetaError.Extract(json));
            }
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error deleting Meta template");
            return false;
        }
    }
}
