using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using wa_api.Features.Billing.Jobs;
using wa_api.Infrastructure.Stripe;

namespace wa_api.Features.Webhooks.Controllers;

/// <summary>
/// Receives Stripe webhook events. Verifies the signature, acknowledges fast (200),
/// then enqueues async processing via Hangfire — same pattern as the Meta webhook receiver.
/// Must be <see cref="AllowAnonymousAttribute"/> (Stripe sends no JWT).
/// The raw body must be read BEFORE ASP.NET model binding so the HMAC covers the original bytes.
/// </summary>
[ApiController]
[Route("webhooks/stripe")]
[AllowAnonymous]
public class StripeWebhookController(
    IOptions<StripeOptions> options,
    ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        string json;
        using (var reader = new global::System.IO.StreamReader(HttpContext.Request.Body))
            json = await reader.ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, options.Value.WebhookSecret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return BadRequest("Invalid Stripe signature.");
        }

        // Ack immediately — never do DB work here.
        BackgroundJob.Enqueue<StripeWebhookProcessingJob>(
            j => j.ProcessAsync(stripeEvent.Type, json, CancellationToken.None));

        logger.LogDebug("Stripe webhook {EventType} {EventId} enqueued", stripeEvent.Type, stripeEvent.Id);
        return Ok();
    }
}
