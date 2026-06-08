using wa_api.Features.Messages.Dtos;

namespace wa_api.Features.Messages;

public interface IMessageService
{
    Task<(IReadOnlyList<MessageResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    Task<MessageResponse> SendAsync(SendMessageRequest request, CancellationToken ct = default);
}
