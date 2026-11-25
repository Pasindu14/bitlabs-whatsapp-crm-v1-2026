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

    /// <summary>
    /// Sends an approved message template — the only thing Meta accepts once the 24-hour window has
    /// closed.
    /// <para>
    /// Always billed: a template is business-initiated by definition, so unlike an in-window reply it
    /// reserves quota and takes a daily-tier permit. The row stored for the inbox carries the RENDERED
    /// text, because a thread showing <c>{{1}}</c> is useless to the agent reading it.
    /// </para>
    /// <para>
    /// Defaulted to a no-op so fakes that only care about plain sends do not have to implement it.
    /// </para>
    /// </summary>
    /// <param name="templateName">Meta's template name, as approved.</param>
    /// <param name="language">The template's language code.</param>
    /// <param name="parameters">Body parameters in order, already validated as single-line.</param>
    /// <param name="renderedBody">The same message with the parameters filled in, for the inbox.</param>
    Task<Message> SendTemplateAsync(
        Contact contact, WabaConnection waba, string templateName, string language,
        IReadOnlyList<string> parameters, string renderedBody, Guid? conversationId,
        CancellationToken ct = default)
        => throw new NotSupportedException("This sender cannot send templates.");

    /// <summary>
    /// Sends a file Meta already holds (uploaded via <c>IWabaMediaUploader</c>) as an image, audio, video or
    /// document. Same pipeline and billing rule as <see cref="SendAsync"/>; the stored message carries the
    /// media metadata. <paramref name="caption"/> is ignored for audio. Defaulted to a throw so fakes that only
    /// send text need not implement it.
    /// </summary>
    Task<Message> SendMediaAsync(
        Contact contact, WabaConnection waba, string mediaType, string metaMediaId, string mimeType,
        string? fileName, string? caption, Guid? conversationId, bool chargeCredit, CancellationToken ct = default)
        => throw new NotSupportedException("This sender cannot send media.");

    /// <summary>
    /// Shows "typing…" on the customer's phone and marks <paramref name="inboundWamid"/> as read (Meta
    /// couples the two). It lasts until the next message we send or 25 seconds, whichever is first, and it
    /// needs the id of a message the customer sent — so it only exists when replying to one.
    /// <para>
    /// Cosmetic and best-effort: it never throws, never writes a row, and takes no rate-limit slot or
    /// credit. Only call it when a reply really is coming — that is Meta's rule, not ours.
    /// </para>
    /// Defaulted to a no-op so fakes that only care about sends don't have to implement it.
    /// </summary>
    Task ShowTypingAsync(WabaConnection waba, string? inboundWamid, CancellationToken ct = default)
        => Task.CompletedTask;
}
