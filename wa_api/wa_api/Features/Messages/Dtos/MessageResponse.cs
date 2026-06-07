using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Messages.Dtos;

public record MessageResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string ContactPhone,
    Guid WabaConnectionId,
    string DisplayPhoneNumber,
    string Body,
    MessageDirection Direction,
    MessageStatus Status,
    string? ExternalMessageId,
    string? ErrorMessage,
    DateTime CreatedAt
);
