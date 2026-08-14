using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Messages;

/// <summary>
/// Low-level WhatsApp send primitive: rate-limit → Meta Graph API → persist the <see cref="Message"/> →
/// meter the subscription. Deliberately knows nothing about conversations (the conversationId is just
/// stamped onto the row), so the standalone endpoint and <c>ConversationService</c> share ONE sending
/// pathway with no DI cycle.
/// <para>
/// Returns the persisted <see cref="Message"/> (Status <c>Sent</c> or <c>Failed</c>). It does NOT throw
/// on a Meta rejection — the caller updates the thread snapshot first, then decides whether a Failed
/// status is fatal. A rate-limit breach still throws before any Meta call or DB write.
/// </para>
/// </summary>
public interface IWhatsAppMessageSender
{
    /// <param name="chargeCredit">
    /// Whether this send consumes one message credit. False for a reply inside an open 24-hour
    /// customer-service window — Meta bills that window as a single conversation, so only the
    /// business-initiated send that opens a thread is charged. A free send takes no reservation at
    /// all, leaving <see cref="Message.MeteredSubscriptionId"/> null so a later delivery-failure
    /// webhook can't refund a credit that was never taken. Intentionally has no default: adding one
    /// would let a new call site compile with accidental billing semantics.
    /// </param>
    Task<Message> SendAsync(
        Contact contact, WabaConnection waba, string body, Guid? conversationId,
        bool chargeCredit, CancellationToken ct = default);
}
