using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Templates.Entities;

/// <summary>
/// The raw bytes of a SAMPLE media file uploaded for a template's media header. Meta only returns an
/// opaque <c>header_handle</c> (not a viewable URL) and discards the file, so we keep our own copy to
/// render a real preview in the builder — both right after upload AND when re-editing a saved template.
/// Served back via <c>GET /templates/media-preview/{id}</c>.
/// <para>
/// Tenant-scoped. One small blob per media header, so it is stored inline as a <c>bytea</c> column —
/// the API has no static-file / object-storage infrastructure to lean on.
/// </para>
/// </summary>
public class TemplateMediaSample : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>MIME type of the stored bytes (e.g. <c>image/png</c>) — echoed as the Content-Type on read.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Original file name (diagnostics / download).</summary>
    public string? FileName { get; set; }

    /// <summary>The Meta <c>header_handle</c> obtained for this same upload (diagnostics).</summary>
    public string? MetaHandle { get; set; }

    /// <summary>The raw file bytes.</summary>
    public byte[] Data { get; set; } = [];
}
