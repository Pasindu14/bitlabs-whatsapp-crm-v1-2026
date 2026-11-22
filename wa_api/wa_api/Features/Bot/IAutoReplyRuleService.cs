using wa_api.Features.Bot.Dtos;

namespace wa_api.Features.Bot;

public interface IAutoReplyRuleService
{
    Task<IReadOnlyList<AutoReplyRuleResponse>> GetAllAsync(CancellationToken ct = default);

    Task<AutoReplyRuleResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AutoReplyRuleResponse> CreateAsync(CreateAutoReplyRuleRequest request, CancellationToken ct = default);

    Task<AutoReplyRuleResponse> UpdateAsync(Guid id, UpdateAutoReplyRuleRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes the rule (<c>IsActive = false</c>), per the platform-wide convention.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
