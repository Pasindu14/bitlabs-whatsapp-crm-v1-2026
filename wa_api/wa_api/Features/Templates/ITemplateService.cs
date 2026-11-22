using wa_api.Features.Templates.Dtos;

namespace wa_api.Features.Templates;

public interface ITemplateService
{
    /// <summary>Paged, searchable list of the caller's templates (newest first by default).</summary>
    Task<(IReadOnlyList<TemplateResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? status, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Loads a single template by id. Throws if not found / not in the caller's company.</summary>
    Task<TemplateResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a Draft template. Validates name/body/examples; throws on duplicate (name, language).</summary>
    Task<TemplateResponse> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default);

    /// <summary>Updates a template. Throws <c>TEMPLATE_NOT_EDITABLE</c> if it is no longer a Draft.</summary>
    Task<TemplateResponse> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken ct = default);

    /// <summary>Submits a Draft to Meta for approval. Stores MetaTemplateId + flips status to Pending.</summary>
    Task<TemplateResponse> SubmitAsync(Guid id, CancellationToken ct = default);

    /// <summary>On-demand poll of Meta for a submitted template's latest status (the "Refresh" button).</summary>
    Task<TemplateResponse> RefreshStatusAsync(Guid id, CancellationToken ct = default);

    /// <summary>Soft-deletes a template (IsActive = false). Step 3 also removes it from Meta when submitted.</summary>
    Task<TemplateResponse> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Approved templates only, ordered by name — what a chatbot follow-up step or handoff alert picks from.
    /// With <paramref name="bodyOnly"/> (the default) media-header, carousel and dynamic-URL templates are
    /// left out, because the bot can only fill body variables (see <see cref="TemplateSendability"/>).
    /// </summary>
    Task<IReadOnlyList<TemplateResponse>> GetApprovedAsync(bool bodyOnly = true, CancellationToken ct = default);

    /// <summary>
    /// Creates the starter follow-up library as drafts for this company. Idempotent — a name that
    /// already exists is left alone — and nothing is sent to Meta: each client submits under their own
    /// number and credentials.
    /// </summary>
    Task<IReadOnlyList<TemplateResponse>> CreateStarterPackAsync(
        Guid? wabaConnectionId = null, CancellationToken ct = default);
}
