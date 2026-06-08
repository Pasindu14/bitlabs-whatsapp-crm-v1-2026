using System.Net.Http.Headers;
using System.Text.Json;
using wa_api.Common.Errors;

namespace wa_api.Features.Templates;

/// <summary>
/// Uploads a SAMPLE media file for a template's media header and returns the resulting
/// <c>header_handle</c> via Meta's Resumable Upload API (Plan 004 Step 4). Uses the platform
/// App access token (<c>Meta:AppId</c>|<c>Meta:AppSecret</c>) — NOT a per-company WABA token.
/// </summary>
public interface IMetaMediaUploader
{
    Task<string> UploadSampleAsync(Stream content, long length, string fileName, string mimeType, CancellationToken ct = default);
}

public class MetaMediaUploader(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<MetaMediaUploader> logger) : IMetaMediaUploader
{
    private const string ApiVersion = "v19.0";

    public async Task<string> UploadSampleAsync(
        Stream content, long length, string fileName, string mimeType, CancellationToken ct = default)
    {
        var appId = config["Meta:AppId"];
        var appSecret = config["Meta:AppSecret"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            throw new BusinessRuleException("META_APP_NOT_CONFIGURED",
                "Media headers require Meta:AppId and Meta:AppSecret to be configured.");

        var appToken = $"{appId}|{appSecret}";
        var client = httpClientFactory.CreateClient("MetaGraph");

        // 1) Create a resumable upload session.
        var sessionUrl = $"{ApiVersion}/{appId}/uploads" +
            $"?file_name={Uri.EscapeDataString(fileName)}" +
            $"&file_length={length}" +
            $"&file_type={Uri.EscapeDataString(mimeType)}" +
            $"&access_token={Uri.EscapeDataString(appToken)}";

        string sessionId;
        using (var sessionReq = new HttpRequestMessage(HttpMethod.Post, sessionUrl))
        using (var sessionRes = await client.SendAsync(sessionReq, ct))
        {
            var sessionJson = await sessionRes.Content.ReadAsStringAsync(ct);
            if (!sessionRes.IsSuccessStatusCode)
                throw new BusinessRuleException("MEDIA_UPLOAD_FAILED",
                    MetaError.Extract(sessionJson) ?? $"Could not start the media upload ({(int)sessionRes.StatusCode}).");

            using var doc = JsonDocument.Parse(sessionJson);
            sessionId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(sessionId))
                throw new BusinessRuleException("MEDIA_UPLOAD_FAILED", "Meta did not return an upload session id.");
        }

        // 2) Upload the file bytes; Meta returns the handle to embed as header_handle.
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, $"{ApiVersion}/{sessionId}");
        uploadReq.Headers.TryAddWithoutValidation("Authorization", $"OAuth {appToken}");
        uploadReq.Headers.TryAddWithoutValidation("file_offset", "0");
        uploadReq.Content = new ByteArrayContent(ms.ToArray());
        if (MediaTypeHeaderValue.TryParse(mimeType, out var parsed))
            uploadReq.Content.Headers.ContentType = parsed;

        using var uploadRes = await client.SendAsync(uploadReq, ct);
        var uploadJson = await uploadRes.Content.ReadAsStringAsync(ct);
        if (!uploadRes.IsSuccessStatusCode)
            throw new BusinessRuleException("MEDIA_UPLOAD_FAILED",
                MetaError.Extract(uploadJson) ?? $"Media upload failed ({(int)uploadRes.StatusCode}).");

        using var uDoc = JsonDocument.Parse(uploadJson);
        var handle = uDoc.RootElement.TryGetProperty("h", out var hEl) ? hEl.GetString() : null;
        if (string.IsNullOrEmpty(handle))
            throw new BusinessRuleException("MEDIA_UPLOAD_FAILED", "Meta did not return a media handle.");

        logger.LogInformation("Uploaded sample media {File} ({Bytes} bytes) → handle obtained.", fileName, length);
        return handle;
    }
}
