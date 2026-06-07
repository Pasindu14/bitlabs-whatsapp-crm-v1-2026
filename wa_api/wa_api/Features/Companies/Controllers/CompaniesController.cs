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

    /// <summary>POST /api/v1/companies — create a company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyRequest request, CancellationToken ct)
    {
        var result = await companyService.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/companies/{id} — soft-delete (deactivate) a company.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await companyService.DeleteAsync(id, ct);
        return Ok(ResponseHelper.Ok(new { deleted = true }, CorrelationId));
    }
}
