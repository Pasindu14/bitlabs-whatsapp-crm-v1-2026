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
        // NOTE: the messaging limit is read from `whatsapp_business_manager_messaging_limit`. Meta DEPRECATED
        // the old `messaging_limit_tier` field — it is still accepted by the Graph API but silently returns
        // nothing, which is what pinned every number at the Tier1K entity default. The deprecated field is no
        // longer requested: it buys us nothing today and would 400 the whole request (taking quality_rating and
        // the status check down with it) the day Meta finishes removing it.
        var url = $"{MetaApiVersion}/{conn.PhoneNumberId}" +
                  "?fields=id,quality_rating,whatsapp_business_manager_messaging_limit,code_verification_status";

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
                var mappedTier = MapMessagingTier(
                    ParseStringField(body, "whatsapp_business_manager_messaging_limit")
                    ?? ParseStringField(body, "messaging_limit_tier"));
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
    /// Map Meta's messaging-limit string (e.g. <c>TIER_1K</c>, <c>TIER_10K</c>, <c>TIER_100K</c>,
    /// <c>TIER_UNLIMITED</c>) to <see cref="MessagingTier"/>. The numeric magnitude is parsed rather than
    /// token-matched, so a tier Meta words differently (<c>TIER_10000</c>) or introduces later still lands on
    /// a sane cap. The result FLOORS to the largest tier we model, so an unfamiliar tier never grants more
    /// than Meta actually allows. Returns null for values carrying no magnitude (<c>TIER_NOT_SET</c>, an
    /// absent field) so the caller leaves the existing tier untouched.
    /// </summary>
    private static MessagingTier? MapMessagingTier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.ToUpperInvariant();

        if (t.Contains("UNLIMITED")) return MessagingTier.Unlimited;

        // Pull the digits out of TIER_250 / TIER_1K / TIER_100K / TIER_10000 …
        var digits = new string(t.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0 || !long.TryParse(digits, out var limit)) return null;

        // …then apply the magnitude suffix the digits were abbreviated with.
        if (t.EndsWith('K')) limit *= 1_000;
        else if (t.EndsWith('M')) limit *= 1_000_000;

        return limit switch
        {
            >= 100_000 => MessagingTier.Tier100K,
            >= 10_000 => MessagingTier.Tier10K,
            >= 1_000 => MessagingTier.Tier1K,
            // TIER_250 and the lower TIER_50 both floor to our smallest modelled tier.
            _ => MessagingTier.Tier250,
        };
    }
}
