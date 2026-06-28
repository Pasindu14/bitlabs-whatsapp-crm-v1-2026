using System.ComponentModel.DataAnnotations;
using wa_api.Features.Campaigns.Entities;

namespace wa_api.Features.Campaigns.Dtos;

public record CreateCampaignRequest(
    [Required][MaxLength(200)] string Name,
    [Required] Guid TemplateId,
    /// <summary>One or more contact lists to send to (union of all members, deduplicated).</summary>
    List<Guid>? ContactListIds,
    /// <summary>Individually selected contact IDs (merged with list members, deduplicated).</summary>
    List<Guid>? ContactIds,
    /// <summary>JSON object mapping variable positions to contact attribute keys.</summary>
    string? VariableMapping,
    [Required] ScheduleType ScheduleType,
    DateTime? ScheduledAt,
    [MaxLength(120)] string? RecurrenceCron,
    /// <summary>
    /// Send to contacts without recorded opt-in consent (bypasses the NO_CONSENT skip gate).
    /// Compliance risk is the operator's — only enable with off-platform consent proof. Defaults false.
    /// </summary>
    bool? OverrideConsentGate = false
);
