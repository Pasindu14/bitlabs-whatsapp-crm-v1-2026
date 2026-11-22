using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Common.Tenancy;
using wa_api.Features.Auth;

namespace wa_api.Features.Bot.Controllers;

/// <summary>What the web app needs to decide whether to show the Chatbot and Calendar screens at all.</summary>
public sealed record BotAccessResponse(
    /// <summary>The platform master switch. Off means the bot answers nobody, whatever the plan says.</summary>
    bool PlatformEnabled,
    /// <summary>The caller's plan includes the chatbot.</summary>
    bool Chatbot,
    /// <summary>The caller's plan includes the calendar.</summary>
    bool Calendar);

/// <summary>
/// Readable by any signed-in tenant user and NOT plan-gated — it is the endpoint that reports the gate, so
/// the sidebar and the inbox can hide chatbot controls for companies that have not bought it.
/// </summary>
[ApiController]
[Route("bot/access")]
[Authorize]
public class BotAccessController(IBotGate gate, IPlanFeatureGate planFeatures, ITenantContext tenant) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/bot/access.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
            return Ok(ResponseHelper.Ok(new BotAccessResponse(gate.GloballyEnabled, false, false), CorrelationId));

        var response = new BotAccessResponse(
            gate.GloballyEnabled,
            await planFeatures.HasFeatureAsync(companyId, Permission.ManageBot, ct),
            await planFeatures.HasFeatureAsync(companyId, Permission.ManageCalendar, ct));

        return Ok(ResponseHelper.Ok(response, CorrelationId));
    }
}
