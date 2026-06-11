using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Messages.Dtos;
using wa_api.Infrastructure.Caching;

namespace wa_api.Features.Messages.Controllers;

[ApiController]
[Route("messages")]
[Authorize(Roles = "CompanyAdmin")]
public class MessagesController(IMessageService service, IIdempotencyService idempotency) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
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
    /// <remarks>
    /// Gated: blocked when the company's subscription is inactive or over quota (PRD 3.1 stub).
    /// Supports idempotency via the optional <c>Idempotency-Key</c> header — supply a client-generated
    /// UUID to guarantee at-most-once delivery even on retries. Keys are remembered for 24 hours.
    /// </remarks>
    [HttpPost]
    [RequireActiveSubscription]
    public async Task<IActionResult> Send(SendMessageRequest request, CancellationToken ct)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetAsync($"msg:send:{idempotencyKey}", ct);
            if (cached is not null)
                return new ContentResult
                {
                    Content = cached.ResponseJson,
                    ContentType = "application/json",
                    StatusCode = cached.StatusCode
                };
        }

        var result = await service.SendAsync(request, ct);
        var envelope = ResponseHelper.Created(result, CorrelationId);

        if (idempotencyKey is not null)
        {
            var json = JsonSerializer.Serialize(envelope, JsonOpts);
            await idempotency.StoreAsync($"msg:send:{idempotencyKey}", StatusCodes.Status201Created, json, ct);
        }

        return StatusCode(StatusCodes.Status201Created, envelope);
    }
}
