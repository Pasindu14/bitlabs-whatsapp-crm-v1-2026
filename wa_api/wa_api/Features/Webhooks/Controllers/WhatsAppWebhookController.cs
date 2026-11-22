using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Features.Webhooks.Ingestion;
using wa_api.Features.Webhooks.Processing;
using wa_api.Features.Webhooks.Signature;
using wa_api.Features.WhatsApp;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Webhooks.Controllers;

/// <summary>
/// Shared Meta WhatsApp webhook endpoint (PRD 4.4) — one URL per platform, routed to companies by
/// phone_number_id during async processing. Public (<see cref="AllowAnonymousAttribute"/>): authenticity
/// comes from the <c>X-Hub-Signature-256</c> HMAC, not a JWT. Verify → persist (durable) → enqueue → 200 fast.
/// </summary>
[ApiController]
[Route("webhooks/whatsapp")]   // RoutePrefixConvention prepends api/v1 → /api/v1/webhooks/whatsapp
[AllowAnonymous]
public sealed class WhatsAppWebhookController(
    IMetaSignatureVerifier verifier,
    IWabaConnectionService connections,
    IWebhookInboxService inbox,
    IBackgroundJobClient jobs,
    IConfiguration config,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    /// <summary>Meta verification handshake — echoes the bare <c>hub.challenge</c> when the token matches.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = config["Meta:WebhookVerifyToken"];
        if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && FixedTimeEquals(verifyToken, expected))
            return Content(challenge ?? string.Empty, "text/plain");   // bare challenge, NOT the ApiResponse envelope

        logger.LogWarning("Webhook verification failed (mode={Mode}).", mode);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>Receives events: HMAC-verify the RAW body → persist durably → enqueue async → return 200.</summary>
    [HttpPost]
    public async Task<IActionResult> Ingest(CancellationToken ct)
    {
        // Read the EXACT bytes the signature was computed over — no model binding / re-serialization.
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Hub-Signature-256"].ToString();

        // Extract phone_number_id first so we can look up the per-connection App Secret.
        var phoneNumberId = TryExtractPhoneNumberId(rawBody);

        bool verified;
        if (phoneNumberId is not null)
        {
            var (conn, undecryptable) = await TryLookupAsync(
                () => connections.GetConnectionByPhoneNumberIdAsync(phoneNumberId, ct), "phone_number_id", phoneNumberId);
            if (undecryptable)
                return Ok();   // same reasoning as the unroutable case below — never 5xx the shared URL
            if (conn is null || string.IsNullOrWhiteSpace(conn.AppSecret))
            {
                // Unroutable but plausibly legitimate: a number onboarded on Meta's side but not yet (or no
                // longer) in our DB. This ONE URL is shared by every tenant, and Meta disables the whole
                // subscription after sustained non-2xx — so a 401 here would degrade ingestion for ALL
                // companies. Ack 200 and drop instead; reserve non-2xx for bad signatures / persistence errors
                // on routable payloads (H7). We can't verify without the secret, so we don't process it.
                logger.LogWarning(
                    "Dropping webhook for unroutable phone_number_id {Id} (no active connection) — acking 200.",
                    phoneNumberId);
                return Ok();
            }
            verified = verifier.Verify(rawBody, signature, conn.AppSecret);
        }
        else
        {
            // No phone_number_id — a WABA-level event, which carries only the WABA id in entry[].id.
            // Route verification to the owning connection's App Secret (M14): a tenant whose Meta app
            // secret differs from the global one would otherwise have all such webhooks rejected.
            // Fall back to the global secret only when the WABA can't be resolved (single-app setups).
            var wabaId = TryExtractWabaId(rawBody);
            var (conn, undecryptable) = wabaId is not null
                ? await TryLookupAsync(() => connections.GetConnectionByWabaIdAsync(wabaId, ct), "waba_id", wabaId)
                : (null, false);
            if (undecryptable)
                return Ok();

            verified = conn is not null && !string.IsNullOrWhiteSpace(conn.AppSecret)
                ? verifier.Verify(rawBody, signature, conn.AppSecret)
                : verifier.Verify(rawBody, signature);
        }

        if (!verified)
        {
            logger.LogWarning("Rejected webhook with invalid/missing X-Hub-Signature-256.");
            return Unauthorized();
        }

        try
        {
            var (id, isNew) = await inbox.PersistAsync(rawBody, phoneNumberId, ct);
            if (isNew)
                jobs.Enqueue<WebhookProcessingJob>(j => j.RunAsync(id, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // The durable inbox write is our only safety net; if it fails after a valid signature, ask Meta
            // to redeliver. This is the single intentional non-2xx for a verified payload.
            logger.LogError(ex, "Failed to persist verified webhook — signalling Meta to redeliver.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    /// <summary>Best-effort first <c>phone_number_id</c> — stored on the inbox row for routing/observability.</summary>
    private static string? TryExtractPhoneNumberId(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var change in changes.EnumerateArray())
                    if (change.TryGetProperty("value", out var value)
                        && value.TryGetProperty("metadata", out var meta)
                        && meta.TryGetProperty("phone_number_id", out var pnid))
                        return pnid.GetString();
            }
        }
        catch (JsonException) { /* malformed — the dispatcher surfaces the real parse error during processing */ }
        return null;
    }

    /// <summary>Best-effort first <c>entry[].id</c> — the WABA id on events without a phone_number_id
    /// (WABA-level events), used to pick the owning connection's App Secret for verification.</summary>
    private static string? TryExtractWabaId(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in entries.EnumerateArray())
                if (entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    return id.GetString();
        }
        catch (JsonException) { /* malformed — verification will fail closed below */ }
        return null;
    }

    /// <summary>
    /// Loads a connection, turning a secret-decryption failure into a logged "undecryptable" result instead of
    /// an exception. The EF value converter decrypts AppSecret/AccessToken while materializing the row, so a
    /// row written under a different <c>Encryption:Key</c> throws here. Left unhandled that was a 500 on the ONE
    /// shared webhook URL, and sustained non-2xx gets Meta to disable the subscription for every tenant.
    /// </summary>
    private async Task<(WabaConnection? Conn, bool Undecryptable)> TryLookupAsync(
        Func<Task<WabaConnection?>> lookup, string keyName, string keyValue)
    {
        try
        {
            return (await lookup(), false);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            logger.LogError(ex,
                "Dropping webhook for {KeyName} {KeyValue}: the connection's stored secrets can't be decrypted " +
                "with the configured Encryption:Key (written under a different key?). Re-enter the Access Token " +
                "and App Secret on the WABA connection — acking 200.", keyName, keyValue);
            return (null, true);
        }
    }

    private static bool FixedTimeEquals(string? a, string b)
    {
        if (string.IsNullOrEmpty(a)) return false;
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
