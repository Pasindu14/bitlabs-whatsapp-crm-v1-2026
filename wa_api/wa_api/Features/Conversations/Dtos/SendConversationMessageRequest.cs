using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>Body for POST /conversations/{id}/messages. The conversation id comes from the route.</summary>
public record SendConversationMessageRequest(
    [Required][MaxLength(4096)] string Body
);
