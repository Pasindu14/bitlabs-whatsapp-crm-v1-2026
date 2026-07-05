using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Messages.Dtos;
using wa_api.Infrastructure.Caching;
using wa_api.Infrastructure.Locking;

namespace wa_api.Features.Messages.Controllers;

[ApiController]
[Route("messages")]
[Authorize(Roles = "CompanyAdmin")]
public class MessagesController(
    IMessageService service,
    IIdempotencyService idempotency,
    IDistributedLockService locks) : ControllerBase
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

        // No key → nothing to dedupe; send directly.
        if (idempotencyKey is null)
            return await SendAndEnvelopeAsync(request, ct);

        var cacheKey = $"msg:send:{idempotencyKey}";

        var cached = await idempotency.GetAsync(cacheKey, ct);
        if (cached is not null)
            return Replay(cached);

        // Serialize concurrent submits of the SAME key so the get→send→store sequence is atomic. Without it,
        // two in-flight duplicates both miss the cache, both send, and the second StoreAsync collides on the
        // key's PK → an unhandled 500 plus a duplicate delivery (M2).
        await using var handle = await locks.AcquireAsync(cacheKey, ct);
        if (handle is null)
        {
            // Another request holds the key. It may have just finished and stored — re-check once before
            // giving up; otherwise tell the client to retry (its retry will hit the cached result).
            var raced = await idempotency.GetAsync(cacheKey, ct);
            if (raced is not null)
                return Replay(raced);

            throw new ConflictException("IDEMPOTENCY_IN_PROGRESS",
                "A request with this Idempotency-Key is still being processed. Retry in a moment.");
        }

        // Re-check inside the lock: the holder may have completed between our first miss and acquiring.
        cached = await idempotency.GetAsync(cacheKey, ct);
        if (cached is not null)
            return Replay(cached);

        var envelopeResult = await SendAndEnvelopeAsync(request, ct);
        await idempotency.StoreAsync(cacheKey, StatusCodes.Status201Created,
            JsonSerializer.Serialize(((ObjectResult)envelopeResult).Value, JsonOpts), ct);
        return envelopeResult;
    }

    private async Task<IActionResult> SendAndEnvelopeAsync(SendMessageRequest request, CancellationToken ct)
    {
        var result = await service.SendAsync(request, ct);
        var envelope = ResponseHelper.Created(result, CorrelationId);
        return StatusCode(StatusCodes.Status201Created, envelope);
    }

    private static ContentResult Replay(IdempotencyResult cached) => new()
    {
        Content = cached.ResponseJson,
        ContentType = "application/json",
        StatusCode = cached.StatusCode,
    };
}
