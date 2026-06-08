using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Users.Dtos;

namespace wa_api.Features.Users.Controllers;

/// <summary>
/// Company-scoped user management. TENANT-LEVEL: every endpoint is restricted to
/// <c>CompanyAdmin</c> and operates ONLY on the caller's own company. The owning company
/// is resolved server-side from the JWT (never the request body), so a CompanyAdmin can
/// only ever create/manage CompanyAdmin/Agent users within their own company.
/// </summary>
[ApiController]
[Route("team-users")]
[Authorize(Roles = "CompanyAdmin")]
public class CompanyUsersController(IUserService userService) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/team-users — paged, searchable list of the company's users.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await userService.GetPagedForCompanyAsync(page, pageSize, search, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/team-users/{id} — single user in the caller's company.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await userService.GetByIdForCompanyAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/team-users — create a user for the caller's own company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyUserRequest request, CancellationToken ct)
    {
        var result = await userService.CreateForCompanyAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/team-users/{id} — update identity + role (password excluded).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCompanyUserRequest request, CancellationToken ct)
    {
        var result = await userService.UpdateForCompanyAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/team-users/{id}/reset-password — set a new password.</summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await userService.ResetPasswordForCompanyAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/team-users/{id}/activate — mark active.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var result = await userService.ActivateForCompanyAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/team-users/{id}/deactivate — mark inactive (soft-delete).</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var result = await userService.DeactivateForCompanyAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
