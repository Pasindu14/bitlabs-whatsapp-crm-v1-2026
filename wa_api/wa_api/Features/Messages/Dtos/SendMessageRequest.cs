using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Messages.Dtos;

public record SendMessageRequest(
    [Required] Guid ContactId,
    [Required] Guid WabaConnectionId,
    [Required][MaxLength(4096)] string Body
);
