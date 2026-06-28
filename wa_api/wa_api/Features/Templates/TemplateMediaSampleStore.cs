using Microsoft.EntityFrameworkCore;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Templates;

/// <summary>
/// Persists and reads back the SAMPLE media bytes for a template's media header so the builder can
/// show a real image preview (the Meta handle is opaque). Tenant scoping is enforced by the global
/// query filter on <see cref="TemplateMediaSample"/>; <c>CompanyId</c> is auto-stamped on insert.
/// </summary>
public interface ITemplateMediaSampleStore
{
    Task<Guid> SaveAsync(byte[] data, string contentType, string? fileName, string? metaHandle, CancellationToken ct = default);
    Task<TemplateMediaSample?> GetAsync(Guid id, CancellationToken ct = default);
}

public class TemplateMediaSampleStore(AppDbContext db) : ITemplateMediaSampleStore
{
    public async Task<Guid> SaveAsync(
        byte[] data, string contentType, string? fileName, string? metaHandle, CancellationToken ct = default)
    {
        var sample = new TemplateMediaSample
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileName = fileName,
            MetaHandle = metaHandle,
            Data = data,
        };
        // CompanyId is auto-stamped from the request's ITenantContext by AuditInterceptor.
        db.Add(sample);
        await db.SaveChangesAsync(ct);
        return sample.Id;
    }

    public Task<TemplateMediaSample?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.TemplateMediaSamples.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
}
