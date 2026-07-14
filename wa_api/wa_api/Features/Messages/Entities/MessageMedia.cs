using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Messages.Entities;

/// <summary>
/// The raw bytes of an inbound media message (image/video/audio/document/sticker), downloaded from Meta and
/// stored so the inbox can render/serve it. Meta only exposes a short-lived signed URL that requires the WABA
/// token, so — exactly like <c>TemplateMediaSample</c> — we keep our own copy as a <c>bytea</c> column (the
/// API has no static-file / object-storage infrastructure to lean on).
/// <para>
/// One row per media <see cref="Message"/> (1:1). Kept in its own table so ordinary <c>Message</c> reads never
/// pull the blob. Served back via <c>GET /api/v1/media/{messageId}</c>; tenant-scoped by the global filter.
/// </para>
/// </summary>
public class MessageMedia : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>The owning message (unique — 1:1).</summary>
    public Guid MessageId { get; set; }

    /// <summary>MIME type of the stored bytes (e.g. <c>image/jpeg</c>) — echoed as the Content-Type on read.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>The raw media bytes.</summary>
    public byte[] Data { get; set; } = [];

    public Message Message { get; set; } = null!;
}
