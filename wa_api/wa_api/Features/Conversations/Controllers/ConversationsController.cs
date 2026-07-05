using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Conversations.Dtos;
using wa_api.Infrastructure.Caching;

namespace wa_api.Features.Conversations.Controllers;

[ApiController]
[Route("conversations")]
// Agents work the inbox (the Agent role is defined as "operates within a company: chats, contacts"), so the
// whole thread plane — list/view/reply/mark-read — is open to CompanyAdmin AND Agent (L10). Tenant isolation
// still holds: agents are company-scoped, so the global query filter limits them to their own company's rows.
[Authorize(Roles = "CompanyAdmin,Agent")]
public class ConversationsController(IConversationService service, IIdempotencyService idempotency) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/conversations — paged, searchable inbox for the caller's company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/conversations/{id} — a single conversation (404 if not visible).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/conversations/{id}/messages — paged thread history (newest-first).</summary>
    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetMessagesAsync(id, page, pageSize, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/conversations — start (or reuse) a conversation with a contact and send the first
    /// message. Gated by an active subscription. Returns the conversation so the client can open the thread.
    /// A closed-window send failure is recorded on the message (shown in the thread), not surfaced as an error.
    /// </summary>
    [HttpPost]
    [RequireActiveSubscription]
    public async Task<IActionResult> Start(StartConversationRequest request, CancellationToken ct)
    {
        var result = await service.StartConversationAsync(request.ContactId, request.WabaConnectionId, request.Body, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/conversations/{id}/messages — reply in a conversation.
    /// Gated by an active subscription. Supports at-most-once delivery via the optional
    /// <c>Idempotency-Key</c> header (a client-generated UUID), remembered for 24 hours.
    /// </summary>
    [HttpPost("{id:guid}/messages")]
    [RequireActiveSubscription]
    public async Task<IActionResult> SendMessage(Guid id, SendConversationMessageRequest request, CancellationToken ct)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetAsync($"conv:send:{id}:{idempotencyKey}", ct);
            if (cached is not null)
                return new ContentResult
                {
                    Content = cached.ResponseJson,
                    ContentType = "application/json",
                    StatusCode = cached.StatusCode
                };
        }

        var result = await service.SendMessageAsync(id, request.Body, ct);
        var envelope = ResponseHelper.Created(result, CorrelationId);

        if (idempotencyKey is not null)
        {
            var json = JsonSerializer.Serialize(envelope, JsonOpts);
            await idempotency.StoreAsync($"conv:send:{id}:{idempotencyKey}", StatusCodes.Status201Created, json, ct);
        }

        return StatusCode(StatusCodes.Status201Created, envelope);
    }

    /// <summary>POST /api/v1/conversations/{id}/read — clear the unread count (agent opened the thread).</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await service.MarkReadAsync(id, ct);
        return Ok(ResponseHelper.Ok(new { id }, CorrelationId));
    }
}
