using wa_api.Features.Campaigns.Dtos;

namespace wa_api.Features.Campaigns;

public interface ICampaignService
{
    Task<(IReadOnlyList<CampaignResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? statusFilter, CancellationToken ct = default);

    Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default);

    Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<CampaignResponse> LaunchAsync(Guid id, CancellationToken ct = default);

    Task<CampaignResponse> PauseAsync(Guid id, CancellationToken ct = default);

    Task<CampaignResponse> ResumeAsync(Guid id, CancellationToken ct = default);

    Task<CampaignResponse> CancelAsync(Guid id, CancellationToken ct = default);

    Task<CampaignStatsResponse> GetStatsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pre-send breakdown of the target audience (sendable / no-consent / opted-out / invalid).</summary>
    Task<CampaignAudienceHealthResponse> GetAudienceHealthAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<CampaignRecipientResponse> Items, int Total)> GetRecipientsAsync(
        Guid id, int page, int pageSize, string? statusFilter, CancellationToken ct = default);

    Task<CampaignResponse> DuplicateAsync(Guid id, CancellationToken ct = default);
}
