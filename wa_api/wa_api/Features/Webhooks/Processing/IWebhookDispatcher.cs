using wa_api.Features.Webhooks.Entities;

namespace wa_api.Features.Webhooks.Processing;

public interface IWebhookDispatcher
{
    /// <summary>
    /// Parses the inbox row's payload and routes each change to its handler(s), resolving the owning
    /// company by phone_number_id. Handlers only MUTATE tracked entities; the caller commits (so the
    /// whole webhook applies atomically). Throws if a handler fails (the job marks the row failed/dead).
    /// </summary>
    Task DispatchAsync(WhatsAppWebhookEvent row, CancellationToken ct = default);
}
