using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace wa_api.Features.ClinicCrm.Client;

/// <inheritdoc />
public sealed class ClinicCrmClient(
    HttpClient http,
    ClinicCrmTokenCache tokens,
    IOptions<ClinicCrmOptions> options,
    ILogger<ClinicCrmClient> logger) : IClinicCrmClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private TimeSpan RefreshMargin => TimeSpan.FromSeconds(Math.Max(0, options.Value.TokenRefreshMarginSeconds));

    public async Task<ClinicCrmResult<ClinicCrmToken>> GetTokenAsync(
        ClinicCrmEndpoint endpoint, CancellationToken ct = default)
    {
        var body = new { clientId = endpoint.ClientId, clientSecret = endpoint.ClientSecret };
        return (await SendRawAsync<ClinicCrmToken>(endpoint, HttpMethod.Post, "token", body, bearer: null, ct)).Result;
    }

    public Task<ClinicCrmResult<IReadOnlyList<ClinicCrmTreatment>>> GetTreatmentsAsync(
        ClinicCrmEndpoint endpoint, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ClinicCrmTreatment>>(endpoint, HttpMethod.Get, "treatments", null, ct);

    public Task<ClinicCrmResult<ClinicCrmCustomer>> SaveCustomerAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmSaveCustomerRequest request, CancellationToken ct = default) =>
        SendAsync<ClinicCrmCustomer>(endpoint, HttpMethod.Post, "customers", request, ct);

    public Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
        ClinicCrmEndpoint endpoint, DateOnly date, CancellationToken ct = default) =>
        SendAsync<ClinicCrmAvailability>(
            endpoint, HttpMethod.Get, $"availability?date={ClinicCrmFormat.Date(date)}", null, ct);

    public Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmBookRequest request, CancellationToken ct = default) =>
        SendAsync<ClinicCrmBookingChange>(endpoint, HttpMethod.Post, "bookings", request, ct);

    public Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetBookingsAsync(
        ClinicCrmEndpoint endpoint, string whatsapp, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ClinicCrmBooking>>(
            endpoint, HttpMethod.Get, $"bookings?whatsapp={Uri.EscapeDataString(whatsapp)}", null, ct);

    public Task<ClinicCrmResult<JsonElement>> CancelBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, ClinicCrmCancelBookingRequest request, CancellationToken ct = default) =>
        SendAsync<JsonElement>(endpoint, HttpMethod.Post, $"bookings/{bookingId}/cancel", request, ct);

    public Task<ClinicCrmResult<ClinicCrmBookingChange>> UpdateBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, ClinicCrmUpdateBookingRequest request, CancellationToken ct = default) =>
        SendAsync<ClinicCrmBookingChange>(endpoint, HttpMethod.Patch, $"bookings/{bookingId}", request, ct);

    // ── plumbing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// An authorised call: reuses the cached token, and on a 401 fetches a fresh one and tries exactly once
    /// more — the document's rule, and the only safe one for a POST that may already have been refused.
    /// </summary>
    private async Task<ClinicCrmResult<T>> SendAsync<T>(
        ClinicCrmEndpoint endpoint, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var key = ClinicCrmTokenCache.KeyFor(endpoint);

        var token = await AcquireTokenAsync(endpoint, key, ct);
        if (!token.IsOk) return token.As<T>();

        var (result, status) = await SendRawAsync<T>(endpoint, method, path, body, token.Data!, ct);

        // Only a 401 is worth a new token. A 403 means the connection has the wrong role, which no token fixes.
        if (status != HttpStatusCode.Unauthorized) return result;

        // Expired or revoked between our check and their answer. One fresh token, one retry.
        tokens.Invalidate(key, token.Data!);

        var fresh = await AcquireTokenAsync(endpoint, key, ct);
        if (!fresh.IsOk) return fresh.As<T>();

        return (await SendRawAsync<T>(endpoint, method, path, body, fresh.Data!, ct)).Result;
    }

    private async Task<ClinicCrmResult<string>> AcquireTokenAsync(
        ClinicCrmEndpoint endpoint, string key, CancellationToken ct)
    {
        if (tokens.TryGet(key, RefreshMargin, out var cached)) return new(ClinicCrmOutcome.Ok, cached);

        var gate = tokens.LockFor(key);
        await gate.WaitAsync(ct);
        try
        {
            // Whoever held the lock before us may have just fetched one.
            if (tokens.TryGet(key, RefreshMargin, out cached)) return new(ClinicCrmOutcome.Ok, cached);

            var result = await GetTokenAsync(endpoint, ct);
            if (!result.IsOk) return result.As<string>();

            var issued = result.Data!;
            if (string.IsNullOrWhiteSpace(issued.AccessToken))
                return new(ClinicCrmOutcome.Unavailable, Message: "The clinic's system returned an empty token.");

            var lifetime = TimeSpan.FromSeconds(issued.ExpiresIn > 0 ? issued.ExpiresIn : 900);
            tokens.Set(key, issued.AccessToken, DateTime.UtcNow + lifetime);

            return new(ClinicCrmOutcome.Ok, issued.AccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private const string Forbidden = "This connection is not a WhatsApp BOT connection. Ask the clinic's admin for one.";

    private async Task<(ClinicCrmResult<T> Result, HttpStatusCode? Status)> SendRawAsync<T>(
        ClinicCrmEndpoint endpoint, HttpMethod method, string path, object? body, string? bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{endpoint.BaseUrl.TrimEnd('/')}/{path}");

        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Never the URL's query or the body — the body of /token is the secret.
            logger.LogWarning("Clinic CRM {Method} {Path} could not be reached: {Error}",
                method, PathOnly(path), ex.Message);
            return (new(ClinicCrmOutcome.Unavailable, Message: "The clinic's system could not be reached."), null);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            var result = Interpret<T>(response.StatusCode, text);

            if (!result.IsOk)
            {
                logger.LogInformation("Clinic CRM {Method} {Path} answered {Status}: {Outcome} {Message}",
                    method, PathOnly(path), (int)response.StatusCode, result.Outcome, result.Message);
            }

            return (result, response.StatusCode);
        }
    }

    /// <summary>
    /// Maps a status code and the <c>{ success, data, message, errors }</c> envelope to a result. Lenient
    /// about shape — a missing <c>errors</c> or <c>data</c> must not turn a clean refusal into a crash.
    /// </summary>
    internal static ClinicCrmResult<T> Interpret<T>(HttpStatusCode status, string body)
    {
        using var document = TryParse(body);
        var root = document?.RootElement ?? default;
        var parsed = root.ValueKind == JsonValueKind.Object;

        var message = parsed && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;

        var field = parsed ? FirstErrorField(root) : null;
        var data = parsed && root.TryGetProperty("data", out var d) ? d : default;
        var success = parsed && root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;

        var code = (int)status;

        if (code is >= 200 and < 300)
        {
            if (!success)
                return new(ClinicCrmOutcome.Rejected, Message: message ?? "The clinic's system refused the request.",
                    ErrorField: field);

            try
            {
                var value = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? default
                    : data.Deserialize<T>(Json);
                return new(ClinicCrmOutcome.Ok, value, message);
            }
            catch (JsonException)
            {
                return new(ClinicCrmOutcome.Unavailable, Message: "The clinic's system sent a reply we could not read.");
            }
        }

        return code switch
        {
            400 => new(ClinicCrmOutcome.Rejected, Message: message ?? "The clinic's system refused the request.",
                ErrorField: field),
            401 => new(ClinicCrmOutcome.NotAuthorised,
                Message: message ?? "The clinic's system did not accept our credentials."),
            403 => new(ClinicCrmOutcome.NotAuthorised, Message: Forbidden),
            404 => new(ClinicCrmOutcome.NotFound, Message: message, ErrorField: field),
            409 => new(ClinicCrmOutcome.Taken, Message: message, ErrorField: field, FreeTimes: FreeTimes(data)),
            _ => new(ClinicCrmOutcome.Unavailable,
                Message: code == 429
                    ? "The clinic's system is busy. Try again in a minute."
                    : message ?? $"The clinic's system answered {code}."),
        };
    }

    private static JsonDocument? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // An HTML error page from a proxy, say. Decided by the status code alone.
            return null;
        }
    }

    private static string? FirstErrorField(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array) return null;

        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String)
                return f.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string>? FreeTimes(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("freeTimes", out var times)
            || times.ValueKind != JsonValueKind.Array)
            return null;

        return [.. times.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!)];
    }

    /// <summary>The path without its query, which can hold a customer's phone number.</summary>
    private static string PathOnly(string path)
    {
        var q = path.IndexOf('?');
        return q < 0 ? path : path[..q];
    }
}
