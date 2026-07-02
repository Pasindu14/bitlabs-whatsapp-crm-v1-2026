using wa_api.Features.Packages.Dtos;
using wa_api.Features.Subscriptions.Dtos;

namespace wa_api.Features.Subscriptions;

public interface ISubscriptionService
{
    /// <summary>Paged list of subscriptions across all companies (SuperAdmin).</summary>
    Task<(IReadOnlyList<SubscriptionResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>The active subscription for a specific company (SuperAdmin). Empty shape if none.</summary>
    Task<SubscriptionResponse> GetForCompanyAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>The caller's own active subscription (tenant path). Empty shape if none.</summary>
    Task<SubscriptionResponse> GetMineAsync(CancellationToken ct = default);

    /// <summary>
    /// Places a company onto a plan (SuperAdmin). Stacks onto a LIVE subscription (adds the new
    /// plan's quota to the balance and extends the expiry) or starts a fresh period from today
    /// when there is none / it is expired / exhausted. Records a <c>SubscriptionPurchase</c> either
    /// way. Throws on unknown company / plan.
    /// </summary>
    Task<SubscriptionResponse> AssignAsync(AssignSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Points a company's active subscription at a different plan (SuperAdmin).</summary>
    Task<SubscriptionResponse> ChangePlanAsync(Guid companyId, ChangePlanRequest request, CancellationToken ct = default);

    /// <summary>Cancels a company's active subscription (SuperAdmin). Throws if none active.</summary>
    Task<SubscriptionResponse> CancelAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Adds a message-credit package to a company's active subscription (SuperAdmin), topping up
    /// its <c>ExtraMessageCredits</c> and recording a <c>PackagePurchase</c>. Throws on unknown
    /// company / package, an inactive package, or no active subscription.
    /// </summary>
    Task<SubscriptionResponse> AddPackageAsync(Guid companyId, AddPackageRequest request, CancellationToken ct = default);

    /// <summary>The package-purchase history for a company, newest first (SuperAdmin).</summary>
    Task<IReadOnlyList<PackagePurchaseResponse>> GetPackageHistoryAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>The subscribe (plan-assignment) history for a company, newest first (SuperAdmin).</summary>
    Task<IReadOnlyList<SubscriptionPurchaseResponse>> GetSubscriptionHistoryAsync(Guid companyId, CancellationToken ct = default);
}
