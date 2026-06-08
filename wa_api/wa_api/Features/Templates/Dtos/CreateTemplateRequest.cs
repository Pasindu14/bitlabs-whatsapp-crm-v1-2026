using System.ComponentModel.DataAnnotations;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/templates</c>. The owning company is resolved from the caller's
/// JWT — never accepted from the body. Category is always Marketing in v1 (set server-side).
/// Structural validation (name format, body, examples) happens in the service.
/// </summary>
public record CreateTemplateRequest(
    [Required] Guid WabaConnectionId,
    [Required, StringLength(512, MinimumLength = 1)] string Name,
    [Required, StringLength(20, MinimumLength = 2)] string Language,
    [Required] TemplateComponents Components
);
