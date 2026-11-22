using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// The HTTP layer against the API document's own sample replies: the token, the envelope, and what each
/// status code becomes.
/// </summary>
public class ClinicCrmClientTests
{
    private static readonly ClinicCrmEndpoint Endpoint =
        new(Guid.NewGuid(), "http://crm.test/api/bot/", "gdc_8f3c", "gds_41ab");

    private const string TokenReply =
        """{ "success": true, "data": { "accessToken": "tok-1", "tokenType": "Bearer", "expiresIn": 900 } }""";

    private const string TreatmentsReply =
        """{ "success": true, "data": [ { "id": 1, "name": "Botox", "description": null } ] }""";

    /// <summary>Answers by path from a queue per path, and records every request.</summary>
    private sealed class FakeCrm : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _replies = [];
        public List<(HttpMethod Method, string Path, string Query, string? Auth, string? Body)> Requests { get; } = [];

        public FakeCrm On(string path, HttpStatusCode status, string body)
        {
            if (!_replies.TryGetValue(path, out var queue)) _replies[path] = queue = new();
            queue.Enqueue(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
            return this;
        }

        public FakeCrm Throw(string path, Exception ex)
        {
            if (!_replies.TryGetValue(path, out var queue)) _replies[path] = queue = new();
            queue.Enqueue(() => throw ex);
            return this;
        }

        public int Count(string path)
        {
            lock (Requests) return Requests.Count(r => r.Path == path);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Func<HttpResponseMessage> reply;
            lock (Requests)
            {
                Requests.Add((request.Method, path, request.RequestUri.Query, request.Headers.Authorization?.ToString(), body));

                var queue = _replies[path];
                // The last reply for a path repeats, so "always 200" needs only one line.
                reply = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
            return reply();
        }
    }

    private static ClinicCrmClient Client(FakeCrm handler, ClinicCrmTokenCache? cache = null) =>
        new(new HttpClient(handler), cache ?? new ClinicCrmTokenCache(),
            Options.Create(new ClinicCrmOptions()), NullLogger<ClinicCrmClient>.Instance);

    [Fact]
    public async Task TheTokenIsFetchedOnce_AndReused()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/treatments", HttpStatusCode.OK, TreatmentsReply);
        var client = Client(crm);

        var first = await client.GetTreatmentsAsync(Endpoint);
        var second = await client.GetTreatmentsAsync(Endpoint);

        Assert.True(first.IsOk);
        Assert.Equal("Botox", Assert.Single(second.Data!).Name);
        Assert.Equal(1, crm.Count("/api/bot/token"));
        Assert.All(crm.Requests.Where(r => r.Path == "/api/bot/treatments"), r => Assert.Equal("Bearer tok-1", r.Auth));

        var tokenBody = JsonDocument.Parse(crm.Requests.First(r => r.Path == "/api/bot/token").Body!).RootElement;
        Assert.Equal("gdc_8f3c", tokenBody.GetProperty("clientId").GetString());
        Assert.Equal("gds_41ab", tokenBody.GetProperty("clientSecret").GetString());
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneTokenRequest()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/treatments", HttpStatusCode.OK, TreatmentsReply);
        var cache = new ClinicCrmTokenCache();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Client(crm, cache).GetTreatmentsAsync(Endpoint)));

        Assert.Equal(1, crm.Count("/api/bot/token"));
    }

    [Fact]
    public async Task A401_GetsAFreshToken_AndRetriesExactlyOnce()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply.Replace("tok-1", "tok-2"))
            .On("/api/bot/treatments", HttpStatusCode.Unauthorized, """{ "success": false, "message": "Token expired" }""")
            .On("/api/bot/treatments", HttpStatusCode.OK, TreatmentsReply);

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.True(result.IsOk);
        Assert.Equal(2, crm.Count("/api/bot/token"));
        Assert.Equal("Bearer tok-2", crm.Requests.Last().Auth);
    }

    [Fact]
    public async Task A401AfterTheRetry_IsNotAuthorised_AndStops()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/treatments", HttpStatusCode.Unauthorized, """{ "success": false, "message": "Revoked" }""");

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.Equal(ClinicCrmOutcome.NotAuthorised, result.Outcome);
        Assert.Equal(2, crm.Count("/api/bot/treatments"));
    }

    [Fact]
    public async Task A403_IsNotRetried()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/treatments", HttpStatusCode.Forbidden, """{ "success": false, "message": "Not a bot" }""");

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.Equal(ClinicCrmOutcome.NotAuthorised, result.Outcome);
        Assert.Equal(1, crm.Count("/api/bot/treatments"));
        Assert.Equal(1, crm.Count("/api/bot/token"));
    }

    [Fact]
    public async Task WrongCredentials_AreNotAuthorised_WithoutCallingTheEndpoint()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.Unauthorized, """{ "success": false, "message": "Invalid client" }""");

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.Equal(ClinicCrmOutcome.NotAuthorised, result.Outcome);
        Assert.Equal("Invalid client", result.Message);
        Assert.Equal(0, crm.Count("/api/bot/treatments"));
    }

    [Fact]
    public async Task A409_IsTaken_WithTheFreeTimes()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/bookings", HttpStatusCode.Conflict, """
                { "success": false,
                  "data": { "date": "2026-10-05", "requestedTime": "16:00", "freeTimes": ["15:00", "16:45", "17:00"] },
                  "message": "16:00 on 2026-10-05 is not available. Choose another time." }
                """);

        var result = await Client(crm).BookAsync(Endpoint,
            new ClinicCrmBookRequest("+971501234567", [1], "2026-10-05", "16:00", null));

        Assert.Equal(ClinicCrmOutcome.Taken, result.Outcome);
        Assert.Equal(["15:00", "16:45", "17:00"], result.FreeTimes);

        // Null fields stay out of the JSON.
        var sent = crm.Requests.Last().Body!;
        Assert.DoesNotContain("notes", sent);
        Assert.Contains("\"startTime\":\"16:00\"", sent);
    }

    [Fact]
    public async Task A400_IsRejected_WithTheMessageAndField()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/bookings", HttpStatusCode.BadRequest, """
                { "success": false, "data": null, "message": "The clinic is closed on 2026-10-09.",
                  "errors": [ { "field": "date", "message": "Closed" } ] }
                """);

        var result = await Client(crm).BookAsync(Endpoint,
            new ClinicCrmBookRequest("+971501234567", [1], "2026-10-09", "16:00", null));

        Assert.Equal(ClinicCrmOutcome.Rejected, result.Outcome);
        Assert.Equal("The clinic is closed on 2026-10-09.", result.Message);
        Assert.Equal("date", result.ErrorField);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ServerTrouble_IsUnavailable(HttpStatusCode status)
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/treatments", status, "<html>bad gateway</html>");

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.Equal(ClinicCrmOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task NoAnswerAtAll_IsUnavailable()
    {
        var crm = new FakeCrm()
            .Throw("/api/bot/token", new HttpRequestException("connection refused"));

        var result = await Client(crm).GetTreatmentsAsync(Endpoint);

        Assert.Equal(ClinicCrmOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ATimeout_IsUnavailable()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .Throw("/api/bot/availability", new TaskCanceledException("timeout"));

        var result = await Client(crm).GetAvailabilityAsync(Endpoint, new DateOnly(2026, 10, 5));

        Assert.Equal(ClinicCrmOutcome.Unavailable, result.Outcome);
        Assert.Contains(crm.Requests, r => r.Path == "/api/bot/availability");
    }

    [Fact]
    public async Task AvailabilityAndBookingLookups_SendTheirQueryCorrectly()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/availability", HttpStatusCode.OK, """
                { "success": true, "data": { "date": "2026-10-05", "open": true, "opensAt": "09:00", "closesAt": "18:00",
                  "durationMinutes": 45, "freeTimes": ["09:00", "16:00"] } }
                """)
            .On("/api/bot/bookings", HttpStatusCode.OK, """{ "success": true, "data": [] }""");
        var client = Client(crm);

        var day = await client.GetAvailabilityAsync(Endpoint, new DateOnly(2026, 10, 5));
        await client.GetBookingsAsync(Endpoint, "+971501234567");

        Assert.Equal(["09:00", "16:00"], day.Data!.FreeTimes);
        Assert.Equal(45, day.Data.DurationMinutes);

        Assert.Equal("?date=2026-10-05", crm.Requests.Single(r => r.Path == "/api/bot/availability").Query);
        Assert.Equal("?whatsapp=%2B971501234567", crm.Requests.Single(r => r.Path == "/api/bot/bookings").Query);
    }

    [Fact]
    public async Task Cancel_PostsTheNumberAndReason_ToTheBookingsCancelRoute()
    {
        var crm = new FakeCrm()
            .On("/api/bot/token", HttpStatusCode.OK, TokenReply)
            .On("/api/bot/bookings/68/cancel", HttpStatusCode.OK,
                """{ "success": true, "data": { "bookingId": 68, "status": "Cancelled" }, "message": "Cancelled." }""");

        var result = await Client(crm).CancelBookingAsync(Endpoint, 68,
            new ClinicCrmCancelBookingRequest("+94711803296", "Travelling"));

        Assert.True(result.IsOk);
        var sent = JsonDocument.Parse(crm.Requests.Last().Body!).RootElement;
        Assert.Equal("+94711803296", sent.GetProperty("whatsapp").GetString());
        Assert.Equal("Travelling", sent.GetProperty("note").GetString());
    }

    [Fact]
    public void PhoneNumbers_GetTheirPlus_AndLoseALeadingDoubleZero()
    {
        Assert.Equal("+971501234567", ClinicCrmFormat.WhatsApp("971501234567"));
        Assert.Equal("+971501234567", ClinicCrmFormat.WhatsApp("00971 50 123 4567"));
        Assert.Equal("+971501234567", ClinicCrmFormat.WhatsApp("+971-50-123-4567"));
    }

    [Fact]
    public void TimesAreSentInDubaiTime_WhateverZoneTheServerRunsIn()
    {
        var (date, time) = ClinicCrmFormat.FromUtc(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), ClinicCrmTestKit.Dubai);

        Assert.Equal("2026-10-05", date);
        Assert.Equal("16:00", time);
        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            ClinicCrmFormat.ToUtc("2026-10-05", "16:00", ClinicCrmTestKit.Dubai));
    }
}
