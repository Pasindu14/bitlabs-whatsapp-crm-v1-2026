using System.ComponentModel.DataAnnotations;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates.Dtos;

/// <summary>
/// Payload for <c>PUT /api/v1/templates/{id}</c>. Allowed only while the template is in Draft
/// (the service rejects edits once submitted to Meta).
/// </summary>
public record UpdateTemplateRequest(
    [Required] Guid WabaConnectionId,
    [Required, StringLength(512, MinimumLength = 1)] string Name,
    [Required, StringLength(20, MinimumLength = 2)] string Language,
    [Required] TemplateComponents Components
);
