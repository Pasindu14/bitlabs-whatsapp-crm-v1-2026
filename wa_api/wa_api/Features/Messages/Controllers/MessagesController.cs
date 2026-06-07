using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Messages.Dtos;

namespace wa_api.Features.Messages.Controllers;

[ApiController]
[Route("messages")]
[Authorize(Roles = "CompanyAdmin")]
public class MessagesController(IMessageService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/messages — paged sent-message history for the caller's company.</summary>
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

    /// <summary>POST /api/v1/messages — send a WhatsApp message to a contact.</summary>
    [HttpPost]
    public async Task<IActionResult> Send(SendMessageRequest request, CancellationToken ct)
    {
        var result = await service.SendAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }
}
