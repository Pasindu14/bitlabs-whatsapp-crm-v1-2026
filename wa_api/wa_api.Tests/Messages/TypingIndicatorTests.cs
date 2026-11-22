using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Features.Messages;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.RateLimiting;
using Xunit;

namespace wa_api.Tests.Messages;

/// <summary>
/// Pins the exact request <see cref="WhatsAppMessageSender.ShowTypingAsync"/> sends to Meta, and that it
/// degrades silently: the indicator is cosmetic, so no Meta failure may ever surface to the caller.
/// </summary>
public class TypingIndicatorTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://graph.facebook.com/") };
    }

    private static readonly WabaConnection Waba = new()
    {
        Id = Guid.NewGuid(),
        PhoneNumberId = "pn-42",
        WabaId = "waba-1",
        DisplayPhoneNumber = "+15550100",
        EncryptedAccessToken = "access-token",
    };

    // The typing path touches none of the send path's dependencies (no DB, rate limiter or meter).
    private static WhatsAppMessageSender NewSender(HttpMessageHandler handler) => new(
        db: null!,
        new Factory(handler),
        rateLimiter: null!,
        Options.Create(new RateLimitOptions()),
        meter: null!,
        NullLogger<WhatsAppMessageSender>.Instance);

    [Fact]
    public async Task PostsMetasTypingPayload_ForTheInboundMessage()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await NewSender(handler).ShowTypingAsync(Waba, "wamid.abc");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://graph.facebook.com/v19.0/pn-42/messages", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("access-token", handler.Request.Headers.Authorization.Parameter);

        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("read", root.GetProperty("status").GetString());
        Assert.Equal("wamid.abc", root.GetProperty("message_id").GetString());
        Assert.Equal("text", root.GetProperty("typing_indicator").GetProperty("type").GetString());
    }

    [Fact]
    public async Task AMetaRejection_DoesNotThrow()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{}}") }));

        await NewSender(handler).ShowTypingAsync(Waba, "wamid.abc");
    }

    [Fact]
    public async Task ANetworkFailure_DoesNotThrow()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("no route"));

        await NewSender(handler).ShowTypingAsync(Waba, "wamid.abc");
    }

    [Fact]
    public async Task ASlowMeta_TimesOutQuietly()
    {
        // Honours cancellation, as the real socket handler does.
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var started = DateTime.UtcNow;
        await NewSender(handler).ShowTypingAsync(Waba, "wamid.abc");

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoWamid_MakesNoCall(string? wamid)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await NewSender(handler).ShowTypingAsync(Waba, wamid);

        Assert.Null(handler.Request);
    }
}
