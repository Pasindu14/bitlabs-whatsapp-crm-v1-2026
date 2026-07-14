using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages.Controllers;

/// <summary>
/// Streams the stored bytes of an inbound media message so the inbox can render it. COMPANY-SCOPED: the same
/// plane as the conversation inbox (CompanyAdmin AND Agent), and the global query filter on
/// <c>MessageMedia</c> restricts every caller to their own company's rows.
/// </summary>
[ApiController]
[Route("media")]
[Authorize(Roles = "CompanyAdmin,Agent")]
public class MediaController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// GET /api/v1/media/{id} — stream a media message's bytes (id = the message id). 404 until the bytes
    /// have been downloaded from Meta (or if the message is text / not visible to the caller).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var media = await db.MessageMedia.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MessageId == id, ct)
            ?? throw new NotFoundException("Media", id);

        // Bytes for a given message never change → let the browser cache them (mirrors template media-preview).
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(media.Data, media.ContentType);
    }
}
