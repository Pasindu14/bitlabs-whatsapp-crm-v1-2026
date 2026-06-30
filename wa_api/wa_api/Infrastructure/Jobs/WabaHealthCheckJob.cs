using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Infrastructure.Jobs;

/// <summary>
/// Polls Meta's Graph API for every active WABA connection and updates
/// Status, LastHealthCheckAt, and HealthCheckErrorMessage.
/// Runs cross-tenant — bypasses the EF query filter intentionally.
/// </summary>
public class WabaHealthCheckJob(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WabaHealthCheckJob> logger)
{
    private const string MetaApiVersion = "v19.0";

    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connections = await db.WabaConnections
            .IgnoreQueryFilters()
            .Where(w => w.IsActive)
            .ToListAsync();

        if (connections.Count == 0)
        {
            logger.LogInformation("WabaHealthCheck: no active connections to check.");
            return;
        }

        logger.LogInformation("WabaHealthCheck: checking {Count} connection(s).", connections.Count);

        var client = httpClientFactory.CreateClient("MetaGraph");
        var now = DateTime.UtcNow;

        foreach (var conn in connections)
        {
            await CheckConnectionAsync(client, conn, now);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("WabaHealthCheck: finished at {Time:o}.", now);
    }

    private async Task CheckConnectionAsync(HttpClient client, WabaConnection conn, DateTime checkedAt)
    {
        var url = $"{MetaApiVersion}/{conn.PhoneNumberId}?fields=id,quality_rating,messaging_limit_tier,code_verification_status";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.EncryptedAccessToken);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                conn.Status = WabaConnectionStatus.Connected;
                conn.HealthCheckErrorMessage = null;

                // Persist Meta's quality rating (surfaced on the 10.2 monitoring dashboard).
                conn.QualityRating = ParseStringField(body, "quality_rating") ?? conn.QualityRating;

                // Auto-sync the messaging tier so the rate limiter self-tunes as Meta upgrades the number.
                // Conservative: only a recognized tier string updates the cap; unknown values are left as-is
                // (we never raise a number's cap based on an unrecognized response).
                var mappedTier = MapMessagingTier(ParseStringField(body, "messaging_limit_tier"));
                if (mappedTier is { } tier && tier != conn.MessagingTier)
                {
                    logger.LogInformation(
                        "WabaHealthCheck: connection {Id} ({Phone}) messaging tier {Old} → {New} (Meta sync).",
                        conn.Id, conn.DisplayPhoneNumber, conn.MessagingTier, tier);
                    conn.MessagingTier = tier;
                }
            }
            else
            {
                var errorCode = ParseMetaErrorCode(body);

                // Meta error 190 = invalid/expired access token.
                conn.Status = errorCode == 190
                    ? WabaConnectionStatus.Invalid
                    : WabaConnectionStatus.Disconnected;

                conn.HealthCheckErrorMessage = ParseMetaErrorMessage(body)
                    ?? $"HTTP {(int)response.StatusCode}";

                logger.LogWarning(
                    "WabaHealthCheck: connection {Id} ({Phone}) returned status {Status} — {Error}",
                    conn.Id, conn.DisplayPhoneNumber, conn.Status, conn.HealthCheckErrorMessage);
            }
        }
        catch (Exception ex)
        {
            conn.Status = WabaConnectionStatus.Disconnected;
            conn.HealthCheckErrorMessage = ex.Message;
            logger.LogError(ex, "WabaHealthCheck: exception polling connection {Id}.", conn.Id);
        }

        conn.LastHealthCheckAt = checkedAt;
    }

    private static int? ParseMetaErrorCode(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code))
                return code.GetInt32();
        }
        catch { /* not JSON or unexpected shape */ }
        return null;
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

    /// <summary>Read a top-level string field (e.g. quality_rating, messaging_limit_tier) from Meta's response.</summary>
    private static string? ParseStringField(string body, string field)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        catch { /* not JSON or unexpected shape */ }
        return null;
    }

    /// <summary>
    /// Map Meta's <c>messaging_limit_tier</c> string (e.g. <c>TIER_1K</c>, <c>TIER_100K</c>,
    /// <c>TIER_UNLIMITED</c>) to <see cref="MessagingTier"/>. Tokens are matched most-specific-first.
    /// Returns null for unrecognized values (<c>TIER_NOT_SET</c>, future tiers, etc.) so the caller leaves
    /// the existing tier untouched — we never raise a number's cap on an unrecognized response.
    /// </summary>
    private static MessagingTier? MapMessagingTier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.ToUpperInvariant();

        if (t.Contains("UNLIMITED")) return MessagingTier.Unlimited;
        if (t.Contains("100K")) return MessagingTier.Tier100K;
        if (t.Contains("10K")) return MessagingTier.Tier10K;
        if (t.Contains("1K")) return MessagingTier.Tier1K;
        // TIER_250 and the lower TIER_50 both floor to our smallest modelled tier.
        if (t.Contains("250") || t.Contains("50")) return MessagingTier.Tier250;
        return null;
    }
}
