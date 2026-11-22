using System.ComponentModel.DataAnnotations;
using wa_api.Features.Bot.Entities;

namespace wa_api.Features.Bot.Dtos;

public record AutoReplyRuleResponse(
    Guid Id,
    string Name,
    AutoReplyTrigger Trigger,
    IReadOnlyList<string> Terms,
    string ReplyBody,
    int Priority,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateAutoReplyRuleRequest(
    [Required, MaxLength(120)] string Name,
    [Required] AutoReplyTrigger Trigger,
    /// <summary>Ignored for Welcome and Fallback triggers, which match on position rather than content.</summary>
    IReadOnlyList<string>? Terms,
    [Required, MaxLength(4096)] string ReplyBody,
    int Priority,
    bool IsEnabled
);

public record UpdateAutoReplyRuleRequest(
    [Required, MaxLength(120)] string Name,
    [Required] AutoReplyTrigger Trigger,
    IReadOnlyList<string>? Terms,
    [Required, MaxLength(4096)] string ReplyBody,
    int Priority,
    bool IsEnabled
);
