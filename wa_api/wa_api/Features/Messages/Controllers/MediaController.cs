using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Messages.OutboundMedia;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages.Controllers;

/// <summary>
/// Streams the stored bytes of a media message so the inbox can render it — received media from Postgres,
/// media sent from the inbox from the VPS disk. COMPANY-SCOPED: the same plane as the conversation inbox
/// (CompanyAdmin AND Agent), and the global query filter on <c>MessageMedia</c> restricts every caller to
/// their own company's rows.
/// </summary>
[ApiController]
[Route("media")]
[Authorize(Roles = "CompanyAdmin,Agent")]
public class MediaController(AppDbContext db, IMediaStorage storage) : ControllerBase
{
    /// <summary>
    /// GET /api/v1/media/{id} — stream a media message's bytes (id = the message id). 404 until the bytes
    /// have been downloaded from Meta (or if the message is text / not visible to the caller); 410 once the
    /// retention sweep has removed the file.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var media = await db.MessageMedia.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MessageId == id, ct);

        if (media is null)
        {
            // No row: either not downloaded yet / not media (404), or already purged (410).
            var purged = await db.Messages.AsNoTracking()
                .AnyAsync(m => m.Id == id && m.MediaPurgedAt != null, ct);
            if (purged) return StatusCode(StatusCodes.Status410Gone);
            throw new NotFoundException("Media", id);
        }

        // Bytes for a given message never change → let the browser cache them (mirrors template media-preview).
        Response.Headers.CacheControl = "private, max-age=86400";

        if (media.StoragePath is null)
            return File(media.Data, media.ContentType, enableRangeProcessing: true);

        if (!storage.Exists(media.StoragePath))
            return StatusCode(StatusCodes.Status410Gone);

        // Range support lets audio/video seek instead of downloading the whole file first.
        return File(storage.OpenRead(media.StoragePath), media.ContentType, enableRangeProcessing: true);
    }
}
