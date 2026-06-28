using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.WhatsApp.Dtos;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.RateLimiting;

namespace wa_api.Features.WhatsApp.Controllers;

/// <summary>
/// Read-only view of a tenant's own active WABA connections, used by CompanyAdmin
/// when composing a message to pick which phone number to send from.
/// </summary>
[ApiController]
[Route("my-waba-connections")]
[Authorize(Roles = "CompanyAdmin")]
public class MyWabaConnectionsController(AppDbContext db, IWabaRateLimiter rateLimiter) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>Fills in each connection's daily send usage (read-only; never blocks the response on a cache blip).</summary>
    private async Task<List<WabaConnectionResponse>> WithDailyUsageAsync(
        List<WabaConnectionResponse> items, CancellationToken ct)
    {
        var result = new List<WabaConnectionResponse>(items.Count);
        foreach (var item in items)
            result.Add(item with { DailySentToday = await rateLimiter.GetDailyUsageAsync(item.PhoneNumberId, ct) });
        return result;
    }

    /// <summary>GET /api/v1/my-waba-connections — active connected connections (message picker).</summary>
    [HttpGet]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        // Tenant query filter already scopes to the caller's company.
        var items = await db.WabaConnections.AsNoTracking()
            .Where(w => w.IsActive && w.Status == WabaConnectionStatus.Connected)
            .OrderBy(w => w.DisplayPhoneNumber)
            .Select(w => new WabaConnectionResponse(
                w.Id, w.CompanyId, w.Company.Name, w.PhoneNumberId, w.WabaId,
                w.DisplayPhoneNumber, w.Status, w.EncryptedAccessToken != "", w.IsActive, w.CreatedAt,
                w.LastHealthCheckAt, w.HealthCheckErrorMessage, w.QualityRating, w.MessagingTier, 0))
            .ToListAsync(ct);

        return Ok(ResponseHelper.Ok(await WithDailyUsageAsync(items, ct), CorrelationId));
    }

    /// <summary>GET /api/v1/my-waba-connections/all — all connections regardless of status (connections page).</summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await db.WabaConnections.AsNoTracking()
            .OrderBy(w => w.DisplayPhoneNumber)
            .Select(w => new WabaConnectionResponse(
                w.Id, w.CompanyId, w.Company.Name, w.PhoneNumberId, w.WabaId,
                w.DisplayPhoneNumber, w.Status, w.EncryptedAccessToken != "", w.IsActive, w.CreatedAt,
                w.LastHealthCheckAt, w.HealthCheckErrorMessage, w.QualityRating, w.MessagingTier, 0))
            .ToListAsync(ct);

        return Ok(ResponseHelper.Ok(await WithDailyUsageAsync(items, ct), CorrelationId));
    }
}
