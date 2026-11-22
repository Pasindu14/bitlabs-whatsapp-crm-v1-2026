using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>
/// Body for POST /conversations/{id}/bot/pause. Omitting the duration parks the thread with the agent
/// until they hand it back — which is what an escalation wants, and the reason this is nullable rather
/// than defaulted.
/// </summary>
public record PauseBotRequest(
    [Range(1, 10080)] int? ResumeAfterMinutes
);
