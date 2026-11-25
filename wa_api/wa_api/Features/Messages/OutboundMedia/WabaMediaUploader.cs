using System.Net.Http.Headers;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Messages.OutboundMedia;

/// <summary>
/// Uploads a file to Meta against a company's phone number and returns the media id to send by. Meta must
/// receive the bytes itself: our own <c>GET /media/{id}</c> is JWT-protected, so a link would never resolve.
/// Media ids expire after ~30 days, which is why this runs per send rather than caching ids.
/// </summary>
public interface IWabaMediaUploader
{
    Task<string> UploadAsync(
        WabaConnection waba, byte[] data, string mimeType, string fileName, CancellationToken ct = default);
}

public sealed class WabaMediaUploader(
    IHttpClientFactory httpClientFactory,
    ILogger<WabaMediaUploader> logger) : IWabaMediaUploader
{
    public async Task<string> UploadAsync(
        WabaConnection waba, byte[] data, string mimeType, string fileName, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        form.Add(new StringContent(mimeType), "type");

        var file = new ByteArrayContent(data);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        form.Add(file, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"v19.0/{waba.PhoneNumberId}/media") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", waba.EncryptedAccessToken);

        try
        {
            using var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                var reason = ExtractError(json);
                logger.LogWarning("Meta media upload rejected ({Status}): {Error}", (int)res.StatusCode, reason);
                throw new BusinessRuleException("MEDIA_UPLOAD_FAILED",
                    reason ?? $"WhatsApp refused the file ({(int)res.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id))
                throw new BusinessRuleException("MEDIA_UPLOAD_FAILED", "WhatsApp did not return a media id.");

            return id;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error uploading media to Meta");
            throw new BusinessRuleException("MEDIA_UPLOAD_FAILED", "Could not reach WhatsApp. Please try again.");
        }
    }

    private static string? ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
            return err.TryGetProperty("error_user_msg", out var u) ? u.GetString()
                 : err.TryGetProperty("message", out var m) ? m.GetString()
                 : null;
        }
        catch
        {
            return null;
        }
    }
}
