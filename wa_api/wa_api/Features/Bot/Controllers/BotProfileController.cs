using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Bot.Dtos;

namespace wa_api.Features.Bot.Controllers;

/// <summary>
/// The AI assistant's configuration. COMPANY-SCOPED: every endpoint runs as the caller's company
/// (CompanyId from the JWT) and needs the <c>manage_bot</c> permission — the same key that already
/// guards the keyword rules, since both configure the same bot.
/// </summary>
[ApiController]
[Route("bot")]
[Authorize]
[RequirePermission(Permission.ManageBot)]
[RequirePlanFeature(Permission.ManageBot)]
[RefreshBotGate]
public class BotProfileController(IBotProfileService service, IBotPreviewService preview) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/bot/profile — the assistant's configuration, or unsaved defaults.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await service.GetAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/bot/profile — upsert the configuration.</summary>
    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile(SaveBotProfileRequest request, CancellationToken ct)
    {
        var result = await service.SaveAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/bot/faqs — all FAQ entries, in prompt order.</summary>
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs(CancellationToken ct)
    {
        var result = await service.GetFaqsAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/bot/faqs — add an entry.</summary>
    [HttpPost("faqs")]
    public async Task<IActionResult> CreateFaq(SaveBotFaqRequest request, CancellationToken ct)
    {
        var result = await service.CreateFaqAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/bot/faqs/{id} — edit an entry.</summary>
    [HttpPut("faqs/{id:guid}")]
    public async Task<IActionResult> UpdateFaq(Guid id, SaveBotFaqRequest request, CancellationToken ct)
    {
        var result = await service.UpdateFaqAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/bot/faqs/{id} — soft-delete an entry.</summary>
    [HttpDelete("faqs/{id:guid}")]
    public async Task<IActionResult> DeleteFaq(Guid id, CancellationToken ct)
    {
        await service.DeleteFaqAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }

    /// <summary>GET /api/v1/bot/links — links the assistant may share, in prompt order.</summary>
    [HttpGet("links")]
    public async Task<IActionResult> GetLinks(CancellationToken ct)
    {
        var result = await service.GetLinksAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/bot/links — add a link.</summary>
    [HttpPost("links")]
    public async Task<IActionResult> CreateLink(SaveBotLinkRequest request, CancellationToken ct)
    {
        var result = await service.CreateLinkAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/bot/links/{id} — edit a link.</summary>
    [HttpPut("links/{id:guid}")]
    public async Task<IActionResult> UpdateLink(Guid id, SaveBotLinkRequest request, CancellationToken ct)
    {
        var result = await service.UpdateLinkAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/bot/links/{id} — soft-delete a link.</summary>
    [HttpDelete("links/{id:guid}")]
    public async Task<IActionResult> DeleteLink(Guid id, CancellationToken ct)
    {
        await service.DeleteLinkAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/bot/preview — run the assistant against a made-up conversation.
    /// <para>
    /// Reads real availability so the times it quotes are genuine, but sends nothing and books nothing.
    /// This is how a tenant tunes the persona without practising on real customers.
    /// </para>
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(BotPreviewRequest request, CancellationToken ct)
    {
        var result = await preview.RunAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/bot/prompt — exactly what the model is given for this business. Read-only.</summary>
    [HttpGet("prompt")]
    public async Task<IActionResult> GetPrompt(CancellationToken ct)
    {
        var result = await preview.GetPromptAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
