using System.Net.Http.Headers;
using System.Text.Json;

namespace wa_api.Features.Messages.InboundMedia;

/// <summary>The bytes + resolved content type of a downloaded inbound media file.</summary>
public sealed record DownloadedMedia(byte[] Data, string ContentType);

/// <summary>
/// Resolves a Meta media id to its bytes. WhatsApp inbound media is a two-step, authenticated fetch:
/// <c>GET /{media-id}</c> returns a short-lived (~5&#160;min) signed URL, and the URL itself must be fetched
/// with the same WABA bearer token. Both steps use the token so the bytes are never publicly reachable.
/// </summary>
public interface IInboundMediaDownloader
{
    /// <summary>Downloads the media, or returns <c>null</c> if any step fails (caller decides whether to retry).</summary>
    Task<DownloadedMedia?> DownloadAsync(string mediaId, string accessToken, CancellationToken ct = default);
}

public sealed class InboundMediaDownloader(
    IHttpClientFactory httpClientFactory,
    ILogger<InboundMediaDownloader> logger) : IInboundMediaDownloader
{
    private const string ApiVersion = "v19.0";

    public async Task<DownloadedMedia?> DownloadAsync(string mediaId, string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(accessToken))
            return null;

        // Longer-timeout client (media can be larger than a JSON API call); base address = graph.facebook.com,
        // but the second GET uses an absolute lookaside URL which overrides it.
        var client = httpClientFactory.CreateClient("MetaMedia");

        try
        {
            // 1) Resolve the media id → { url, mime_type }.
            string? mediaUrl;
            string? mimeType;
            using (var metaReq = new HttpRequestMessage(HttpMethod.Get, $"{ApiVersion}/{mediaId}"))
            {
                metaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var metaRes = await client.SendAsync(metaReq, ct);
                var json = await metaRes.Content.ReadAsStringAsync(ct);
                if (!metaRes.IsSuccessStatusCode)
                {
                    logger.LogWarning("Media id {MediaId} lookup returned {Status}: {Body}",
                        mediaId, (int)metaRes.StatusCode, Trim(json));
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                mediaUrl = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
                mimeType = doc.RootElement.TryGetProperty("mime_type", out var mt) ? mt.GetString() : null;
            }

            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                logger.LogWarning("Media id {MediaId} lookup returned no url.", mediaId);
                return null;
            }

            // 2) Download the bytes from the signed URL (also token-gated). A User-Agent is required — the
            // lookaside CDN rejects requests without one.
            using var byteReq = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            byteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            byteReq.Headers.UserAgent.ParseAdd("wa_api/1.0");
            using var byteRes = await client.SendAsync(byteReq, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!byteRes.IsSuccessStatusCode)
            {
                logger.LogWarning("Media id {MediaId} byte download returned {Status}.", mediaId, (int)byteRes.StatusCode);
                return null;
            }

            var bytes = await byteRes.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
            {
                logger.LogWarning("Media id {MediaId} byte download was empty.", mediaId);
                return null;
            }

            // Prefer the mime the lookup reported; fall back to the download's Content-Type, then octet-stream.
            var contentType = FirstNonBlank(
                mimeType,
                byteRes.Content.Headers.ContentType?.MediaType) ?? "application/octet-stream";

            return new DownloadedMedia(bytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Media id {MediaId} download failed.", mediaId);
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
}
