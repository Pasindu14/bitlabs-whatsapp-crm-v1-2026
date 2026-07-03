using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates.Dtos;

/// <summary>A template as returned by the list/detail/create/update/submit/refresh endpoints.</summary>
public record TemplateResponse(
    Guid Id,
    Guid WabaConnectionId,
    string? DisplayPhoneNumber,
    string WabaId,
    string Name,
    string Language,
    TemplateCategory Category,
    TemplateParameterFormat ParameterFormat,
    TemplateComponents Components,
    TemplateStatus Status,
    string? MetaTemplateId,
    string? RejectionReason,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? LastSyncedAt,
    bool IsActive,
    DateTime CreatedAt
);
