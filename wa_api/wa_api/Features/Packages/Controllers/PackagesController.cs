using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Packages.Dtos;

namespace wa_api.Features.Packages.Controllers;

/// <summary>
/// Message-credit package catalog management. PLATFORM-LEVEL: every endpoint is restricted to
/// <c>SuperAdmin</c>. Packages are shared across all tenants (no <c>CompanyId</c>); a company
/// receives a package's credits when SuperAdmin adds it via
/// <c>POST /api/v1/subscriptions/company/{companyId}/packages</c>.
/// </summary>
[ApiController]
[Route("packages")]
[Authorize(Roles = "SuperAdmin")]
public class PackagesController(IPackageService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/packages — paged, searchable list.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/packages/{id} — single package.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/packages — create a package.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreatePackageRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/packages/{id} — update editable fields.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdatePackageRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/packages/{id}/activate — mark active.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var result = await service.ActivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/packages/{id}/deactivate — mark inactive (soft-delete).</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var result = await service.DeactivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
