using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Templates.Dtos;

namespace wa_api.Features.Templates.Controllers;

/// <summary>
/// Template management for a tenant. COMPANY-SCOPED: every endpoint runs as the caller's company
/// (CompanyId from the JWT) and is restricted to <c>CompanyAdmin</c>.
/// <para>Follow-up: also admit Agents holding the <c>manage_template</c> permission (Plan 004).</para>
/// </summary>
[ApiController]
[Route("templates")]
[Authorize(Roles = "CompanyAdmin")]
public class TemplatesController(
    ITemplateService service,
    IMetaMediaUploader mediaUploader,
    ITemplateMediaSampleStore mediaSamples) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/templates — paged, searchable list (optional <c>status</c> filter).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, status, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/templates/{id} — single template with its full component tree.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/templates — create a Draft template.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateTemplateRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/templates/{id} — edit a Draft template (rejected once submitted).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateTemplateRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/templates/{id}/submit — submit a Draft to Meta for approval.</summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var result = await service.SubmitAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/templates/{id}/refresh-status — poll Meta for the latest approval status.</summary>
    [HttpPost("{id:guid}/refresh-status")]
    public async Task<IActionResult> RefreshStatus(Guid id, CancellationToken ct)
    {
        var result = await service.RefreshStatusAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/templates/{id} — soft-delete (and remove from Meta once submitted).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/templates/media-handle — upload a sample media file and get a Meta
    /// handle to use as a media header's example (Plan 004 Step 4). We also persist our own copy of
    /// the bytes and return its <c>previewId</c> so the builder can render a real preview — Meta's
    /// handle is opaque and can't be displayed.</summary>
    [HttpPost("media-handle")]
    public async Task<IActionResult> UploadMediaHandle(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["A non-empty file is required."] });

        // Buffer once: the same bytes go to Meta (for the header_handle) and to our own store (for preview).
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var handle = await mediaUploader.UploadSampleAsync(
            new MemoryStream(bytes), bytes.LongLength, file.FileName, file.ContentType, ct);
        var previewId = await mediaSamples.SaveAsync(bytes, file.ContentType, file.FileName, handle, ct);

        return Ok(ResponseHelper.Ok(new Dtos.MediaHandleResponse(handle, previewId), CorrelationId));
    }

    /// <summary>GET /api/v1/templates/media-preview/{id} — stream a previously-uploaded sample media
    /// file so the builder can show a real header preview. Tenant-scoped by the query filter.</summary>
    [HttpGet("media-preview/{id:guid}")]
    public async Task<IActionResult> GetMediaPreview(Guid id, CancellationToken ct)
    {
        var sample = await mediaSamples.GetAsync(id, ct)
            ?? throw new NotFoundException("TemplateMediaSample", id);

        // Bytes for a given id never change → let the browser cache them.
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(sample.Data, sample.ContentType);
    }
}
