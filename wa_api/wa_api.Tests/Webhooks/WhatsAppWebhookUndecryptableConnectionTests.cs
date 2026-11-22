using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.Webhooks.Controllers;
using wa_api.Features.Webhooks.Ingestion;
using wa_api.Features.Webhooks.Signature;
using wa_api.Features.WhatsApp;
using Xunit;

namespace wa_api.Tests.Webhooks;

/// <summary>
/// Pins that a WABA connection whose stored secrets can't be decrypted (row written under a different
/// Encryption:Key) makes the shared webhook ack 200 and drop, instead of throwing a 500 — sustained non-2xx
/// gets Meta to disable the subscription for every tenant. Nothing may be persisted or verified.
/// </summary>
public class WhatsAppWebhookUndecryptableConnectionTests
{
    private const string MessagePayload =
        """{"object":"whatsapp_business_account","entry":[{"id":"waba-1","changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"pn-1"}}}]}]}""";

    private const string WabaLevelPayload =
        """{"object":"whatsapp_business_account","entry":[{"id":"waba-1","changes":[{"field":"message_template_status_update","value":{}}]}]}""";

    /// <summary>
    /// Proxy for interfaces the test only partly cares about: <see cref="Handler"/> answers the calls it knows,
    /// and anything else records the method name so the test can assert it was never reached.
    /// </summary>
    public class Fake<T> : DispatchProxy where T : class
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            return Handler?.Invoke(method, args)
                ?? throw new InvalidOperationException($"Unexpected call to {typeof(T).Name}.{method.Name}");
        }

        public static (T Proxy, Fake<T> Fake) Create(Func<MethodInfo, object?[]?, object?>? handler = null)
        {
            var proxy = DispatchProxy.Create<T, Fake<T>>();
            var fake = (Fake<T>)(object)proxy;
            fake.Handler = handler;
            return (proxy, fake);
        }
    }

    private static (WhatsAppWebhookController Controller, Fake<IMetaSignatureVerifier> Verifier, Fake<IWebhookInboxService> Inbox)
        Build(string body)
    {
        // Every lookup behaves like EF materializing a row encrypted under another key.
        var (connections, _) = Fake<IWabaConnectionService>.Create((_, _) =>
            throw new AuthenticationTagMismatchException());
        var (verifier, verifierFake) = Fake<IMetaSignatureVerifier>.Create();
        var (inbox, inboxFake) = Fake<IWebhookInboxService>.Create();
        var (jobs, _) = Fake<IBackgroundJobClient>.Create();

        var controller = new WhatsAppWebhookController(
            verifier, connections, inbox, jobs,
            new ConfigurationBuilder().Build(),
            NullLogger<WhatsAppWebhookController>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Request.Headers["X-Hub-Signature-256"] = "sha256=irrelevant";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, verifierFake, inboxFake);
    }

    [Theory]
    [InlineData(MessagePayload)]      // routed by phone_number_id
    [InlineData(WabaLevelPayload)]    // routed by WABA id
    public async Task Undecryptable_connection_acks_200_and_drops(string body)
    {
        var (controller, verifier, inbox) = Build(body);

        var result = await controller.Ingest(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(verifier.Calls);   // no secret to verify with
        Assert.Empty(inbox.Calls);      // nothing persisted
    }
}
