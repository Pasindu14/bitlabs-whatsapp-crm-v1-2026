using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Features.Subscriptions.Jobs;
using wa_api.Infrastructure.PayHere;

namespace wa_api.Features.Webhooks.Controllers;

/// <summary>
/// Receives PayHere's notify callback (server-to-server payment outcome). Verifies the md5sig,
/// acknowledges fast (200), then enqueues async processing via Hangfire — same pattern as
/// <see cref="StripeWebhookController"/>. Must be <see cref="AllowAnonymousAttribute"/> (PayHere
/// sends no JWT). PayHere posts <c>application/x-www-form-urlencoded</c>, not JSON like Stripe.
/// </summary>
[ApiController]
[Route("webhooks/payhere")]
[AllowAnonymous]
public class PayHereWebhookController(
    IPayHereService payHereService,
    ILogger<PayHereWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        var form = await Request.ReadFormAsync();

        if (!payHereService.VerifyNotifySignature(form))
        {
            logger.LogWarning("PayHere webhook signature verification failed for order {OrderId}",
                form["order_id"].ToString());
            return BadRequest("Invalid PayHere signature.");
        }

        var fields = form.Keys.ToDictionary(k => k, k => form[k].ToString());
        var json = JsonSerializer.Serialize(fields);

        // Ack immediately — never do DB work here.
        BackgroundJob.Enqueue<PayHereWebhookProcessingJob>(j => j.ProcessAsync(json, CancellationToken.None));

        logger.LogDebug("PayHere webhook order {OrderId} status {StatusCode} enqueued",
            form["order_id"].ToString(), form["status_code"].ToString());
        return Ok();
    }
}
