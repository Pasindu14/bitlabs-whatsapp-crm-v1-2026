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
        var url = $"{MetaApiVersion}/{conn.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating,code_verification_status";

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
}
