using wa_api.Features.Webhooks.Payloads;

namespace wa_api.Features.Webhooks.Handlers;

/// <summary>
/// Handles one kind of Meta webhook change. Implementations are registered under this interface so the
/// dispatcher receives them all via <c>IEnumerable</c> and routes each change to whoever <see cref="CanHandle"/>.
/// <para>
/// Handlers MUST only MUTATE tracked entities — they never call SaveChanges. The job's "mark processed"
/// step performs the single SaveChanges, so the whole webhook batch commits atomically (and a failed
/// batch is discarded rather than half-applied).
/// </para>
/// </summary>
public interface IWebhookEventHandler
{
    bool CanHandle(string field, WebhookChange change);
    Task HandleAsync(WebhookContext ctx, CancellationToken ct = default);
}
