using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using wa_api.Features.Campaigns.Entities;

namespace wa_api.Features.Campaigns.Dtos;

public record CreateCampaignRequest(
    [Required][MaxLength(200)] string Name,
    [Required] Guid TemplateId,
    [Required] Guid ContactListId,
    /// <summary>JSON object mapping variable positions to contact attribute keys.</summary>
    string? VariableMapping,
    [Required] ScheduleType ScheduleType,
    DateTime? ScheduledAt,
    [MaxLength(120)] string? RecurrenceCron
);
