using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Notifications.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Notifications.Controllers;

[ApiController]
[Route("notifications")]
[Authorize(Roles = "CompanyAdmin")]
public class NotificationsController(
    AppDbContext db,
    INotificationService notificationService) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    private Guid CompanyId => Guid.Parse(User.FindFirstValue("companyId")!);

    /// <summary>GET /api/v1/notifications?page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Notifications.OrderByDescending(n => n.CreatedAt);

        var total = await query.CountAsync(ct);
        var unread = await db.Notifications.CountAsync(n => !n.IsRead, ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationResponse(
                n.Id, n.CampaignId, n.TemplateId, n.Type.ToString(), n.Title, n.Body, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

        return Ok(ResponseHelper.Ok(new NotificationListResponse(items, unread, total), CorrelationId));
    }

    /// <summary>PATCH /api/v1/notifications/{id}/read</summary>
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notificationService.MarkReadAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }

    /// <summary>PATCH /api/v1/notifications/read-all</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notificationService.MarkAllReadAsync(CompanyId, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }
}
