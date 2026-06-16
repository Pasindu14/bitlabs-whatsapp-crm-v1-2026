using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Campaigns.Dtos;

namespace wa_api.Features.Campaigns.Controllers;

[ApiController]
[Route("campaigns")]
[Authorize]
[RequirePermission(Permission.ScheduleCampaign)]
public class CampaignsController(ICampaignService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/campaigns — paged campaign list for the caller's company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, status, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/campaigns/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns — create a new Draft campaign.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateCampaignRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/campaigns/{id} — update a Draft campaign.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCampaignRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/campaigns/{id} — soft-delete a Draft or Cancelled campaign.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return Ok(ResponseHelper.Ok<string>(null!, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns/{id}/launch — validate template + enqueue send job.</summary>
    [HttpPost("{id:guid}/launch")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Launch(Guid id, CancellationToken ct)
    {
        var result = await service.LaunchAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns/{id}/pause — pause a Running campaign.</summary>
    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
    {
        var result = await service.PauseAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns/{id}/resume — resume a Paused campaign from where it left off.</summary>
    [HttpPost("{id:guid}/resume")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var result = await service.ResumeAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns/{id}/cancel — cancel and flip remaining Queued recipients to Skipped.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var result = await service.CancelAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/campaigns/{id}/stats — real-time delivery rollup.</summary>
    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> GetStats(Guid id, CancellationToken ct)
    {
        var result = await service.GetStatsAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/campaigns/{id}/recipients — paged per-recipient status.</summary>
    [HttpGet("{id:guid}/recipients")]
    public async Task<IActionResult> GetRecipients(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetRecipientsAsync(id, page, pageSize, status, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>POST /api/v1/campaigns/{id}/duplicate — clone as a new Draft.</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        var result = await service.DuplicateAsync(id, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }
}
