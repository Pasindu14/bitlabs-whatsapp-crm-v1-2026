using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>Body for POST /conversations — start (or reuse) a conversation with a contact.</summary>
public record StartConversationRequest(
    [Required] Guid ContactId,
    [Required] Guid WabaConnectionId,
    [Required][MaxLength(4096)] string Body
);
