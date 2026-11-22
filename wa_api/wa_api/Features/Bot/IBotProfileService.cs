using wa_api.Features.Bot.Dtos;

namespace wa_api.Features.Bot;

/// <summary>
/// The tenant's AI assistant configuration: one profile plus its FAQ entries.
/// </summary>
public interface IBotProfileService
{
    /// <summary>
    /// The company's profile, or an unsaved default when it has none. Never 404s — a company that has
    /// not configured the bot is in a normal state, not a missing one.
    /// </summary>
    Task<BotProfileResponse> GetAsync(CancellationToken ct = default);

    /// <summary>Creates or replaces the company's single profile.</summary>
    Task<BotProfileResponse> SaveAsync(SaveBotProfileRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<BotFaqResponse>> GetFaqsAsync(CancellationToken ct = default);

    Task<BotFaqResponse> CreateFaqAsync(SaveBotFaqRequest request, CancellationToken ct = default);

    Task<BotFaqResponse> UpdateFaqAsync(Guid id, SaveBotFaqRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes the entry (<c>IsActive = false</c>), per the platform-wide convention.</summary>
    Task DeleteFaqAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BotLinkResponse>> GetLinksAsync(CancellationToken ct = default);

    Task<BotLinkResponse> CreateLinkAsync(SaveBotLinkRequest request, CancellationToken ct = default);

    Task<BotLinkResponse> UpdateLinkAsync(Guid id, SaveBotLinkRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes the link (<c>IsActive = false</c>).</summary>
    Task DeleteLinkAsync(Guid id, CancellationToken ct = default);
}
