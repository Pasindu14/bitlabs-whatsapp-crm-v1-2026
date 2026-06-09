using wa_api.Features.Webhooks.Payloads;

namespace wa_api.Features.Webhooks.Handlers;

/// <summary>
/// PRD 7.3 SEAM — inbound customer messages. STUB for now: it is wired into the dispatcher (so inbound
/// events already flow through the verified, queued pipeline) but only logs. Phase 7 replaces the body
/// with Conversation-window handling + a SignalR push via <c>IHubContext</c>; the receiver, dispatcher,
/// and queue stay unchanged.
/// </summary>
public sealed class InboundMessageWebhookHandler(ILogger<InboundMessageWebhookHandler> logger)
    : IWebhookEventHandler
{
    public bool CanHandle(string field, WebhookChange change)
        => field == "messages" && change.Value?.Messages is { Count: > 0 };

    public Task HandleAsync(WebhookContext ctx, CancellationToken ct = default)
    {
        var phoneNumberId = ctx.Change.Value?.Metadata?.PhoneNumberId;
        foreach (var m in ctx.Change.Value!.Messages!)
            logger.LogInformation(
                "Inbound message {Wamid} from {From} (type={Type}, phoneNumberId={PhoneNumberId}, company={CompanyId}) — deferred to Phase 7 (SignalR + Conversation).",
                m.Id, m.From, m.Type, phoneNumberId, ctx.CompanyId);
        return Task.CompletedTask;
    }
}
