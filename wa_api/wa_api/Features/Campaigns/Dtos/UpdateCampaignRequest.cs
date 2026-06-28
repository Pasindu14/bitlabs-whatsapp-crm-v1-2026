using System.ComponentModel.DataAnnotations;
using wa_api.Features.Campaigns.Entities;

namespace wa_api.Features.Campaigns.Dtos;

public record UpdateCampaignRequest(
    [Required][MaxLength(200)] string Name,
    [Required] Guid TemplateId,
    List<Guid>? ContactListIds,
    List<Guid>? ContactIds,
    string? VariableMapping,
    [Required] ScheduleType ScheduleType,
    DateTime? ScheduledAt,
    [MaxLength(120)] string? RecurrenceCron,
    /// <summary>Send to contacts without recorded opt-in (bypasses the NO_CONSENT gate). Defaults false.</summary>
    bool? OverrideConsentGate = false
);
