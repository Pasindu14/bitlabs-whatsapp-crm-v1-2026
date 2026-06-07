using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.WhatsApp.Dtos;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.WhatsApp.Controllers;

/// <summary>
/// Read-only view of a tenant's own active WABA connections, used by CompanyAdmin
/// when composing a message to pick which phone number to send from.
/// </summary>
[ApiController]
[Route("my-waba-connections")]
[Authorize(Roles = "CompanyAdmin")]
public class MyWabaConnectionsController(AppDbContext db) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/my-waba-connections — active connections for the caller's company.</summary>
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
                w.LastHealthCheckAt, w.HealthCheckErrorMessage))
            .ToListAsync(ct);

        return Ok(ResponseHelper.Ok(items, CorrelationId));
    }
}
