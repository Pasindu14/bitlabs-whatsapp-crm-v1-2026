using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Companies.Dtos;

namespace wa_api.Features.Companies.Controllers;

/// <summary>
/// Company (tenant) management. PLATFORM-LEVEL: every endpoint is restricted to
/// <c>SuperAdmin</c> — CompanyAdmin/Agent get a 403 from the bearer pipeline.
/// </summary>
[ApiController]
[Route("companies")]
[Authorize(Roles = "SuperAdmin")]
public class CompaniesController(ICompanyService companyService) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/companies — paged, searchable list.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await companyService.GetPagedAsync(page, pageSize, search, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/companies/{id} — single company.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await companyService.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/companies — create a company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyRequest request, CancellationToken ct)
    {
        var result = await companyService.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>POST /api/v1/companies/provision — atomically create a company and its first CompanyAdmin.</summary>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision(ProvisionCompanyRequest request, CancellationToken ct)
    {
        var result = await companyService.ProvisionAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/companies/{id} — update all editable fields.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCompanyRequest request, CancellationToken ct)
    {
        var result = await companyService.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/companies/{id}/activate — mark active.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var result = await companyService.ActivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/companies/{id}/deactivate — mark inactive (soft-delete).</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var result = await companyService.DeactivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
