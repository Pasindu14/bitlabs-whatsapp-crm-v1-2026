using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;

namespace wa_api.Features.Admin.Logging;

[ApiController]
[Route("admin/logs")]
[Authorize(Roles = "SuperAdmin")]
public sealed class LogsController(InMemoryLogBuffer buffer) : ControllerBase
{
    /// <summary>
    /// Returns recent log entries. Pass <c>since</c> (ISO-8601 UTC) to get only entries
    /// newer than that timestamp (incremental polling). Pass <c>tail</c> to cap the result.
    /// </summary>
    [HttpGet]
    public IActionResult Get([FromQuery] int tail = 200, [FromQuery] DateTime? since = null)
    {
        IReadOnlyList<LogEntry> entries = since.HasValue
            ? buffer.GetSince(since.Value)
            : buffer.GetRecent(tail);

        return Ok(new ApiResponse<IReadOnlyList<LogEntry>>(true, entries, null, HttpContext.TraceIdentifier));
    }
}
