using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.ClinicCrm.Dtos;
using wa_api.Features.ClinicCrm.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.ClinicCrm;

/// <inheritdoc />
public sealed class ClinicCrmSettingsService(
    AppDbContext db,
    ITenantContext tenant,
    IClinicCrmClient client) : IClinicCrmSettingsService
{
    public async Task<ClinicCrmSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        RequireCompanyId();
        return await BuildAsync(await LoadAsync(tracked: false, ct), ct);
    }

    public async Task<ClinicCrmSettingsResponse> SaveConnectionAsync(
        SaveClinicCrmConnectionRequest request, CancellationToken ct = default)
    {
        var companyId = RequireCompanyId();

        var baseUrl = request.BaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new BusinessRuleException("CLINIC_CRM_URL_INVALID",
                "Enter the full API address, e.g. http://169.58.92.105:3110/api/bot.");

        var clientId = request.ClientId.Trim();
        var secret = request.ClientSecret?.Trim();

        var connection = await LoadAsync(tracked: true, ct);
        if (connection is null)
        {
            if (string.IsNullOrEmpty(secret))
                throw new BusinessRuleException("CLINIC_CRM_SECRET_REQUIRED", "Enter the client secret.");

            connection = new ClinicCrmConnection { CompanyId = companyId };
            db.ClinicCrmConnections.Add(connection);
        }

        var credentialsChanged = connection.BaseUrl != baseUrl
            || connection.ClientId != clientId
            || !string.IsNullOrEmpty(secret);

        connection.BaseUrl = baseUrl;
        connection.ClientId = clientId;
        if (!string.IsNullOrEmpty(secret)) connection.ClientSecret = secret;
        connection.IsEnabled = request.IsEnabled;
        connection.NotifyOnMessages = request.NotifyOnMessages;

        // The old health says nothing about new credentials.
        if (credentialsChanged)
        {
            connection.LastSuccessAt = null;
            connection.LastError = null;
            connection.LastErrorAt = null;
        }

        await db.SaveChangesAsync(ct);
        return await BuildAsync(connection, ct);
    }

    public async Task DeleteConnectionAsync(CancellationToken ct = default)
    {
        RequireCompanyId();

        var connection = await LoadAsync(tracked: true, ct);
        if (connection is null) return;   // idempotent

        // Soft-deleted with the secret wiped: nothing left that could still call the clinic.
        connection.IsActive = false;
        connection.IsEnabled = false;
        connection.ClientSecret = string.Empty;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClinicCrmTestResponse> TestAsync(CancellationToken ct = default)
    {
        RequireCompanyId();

        var connection = await LoadAsync(tracked: true, ct)
            ?? throw new BusinessRuleException("CLINIC_CRM_NOT_CONFIGURED", "Save the connection details first.");

        var endpoint = ToEndpoint(connection);

        var token = await client.GetTokenAsync(endpoint, ct);
        if (!token.IsOk) return await RecordAsync(connection, Failed(token), ct);

        var treatments = await client.GetTreatmentsAsync(endpoint, ct);
        if (!treatments.IsOk) return await RecordAsync(connection, Failed(treatments), ct);

        var list = Map(treatments.Data);
        return await RecordAsync(connection,
            new ClinicCrmTestResponse(true, $"Connected. The clinic has {list.Count} treatment(s).", list), ct);
    }

    public async Task<IReadOnlyList<ClinicCrmTreatmentResponse>> GetTreatmentsAsync(CancellationToken ct = default)
    {
        RequireCompanyId();

        var connection = await LoadAsync(tracked: false, ct)
            ?? throw new BusinessRuleException("CLINIC_CRM_NOT_CONFIGURED", "Save the connection details first.");

        var result = await client.GetTreatmentsAsync(ToEndpoint(connection), ct);
        if (!result.IsOk)
            throw new BusinessRuleException("CLINIC_CRM_UNAVAILABLE",
                result.Message ?? "The clinic's system could not be reached.");

        return Map(result.Data);
    }

    public async Task<ClinicCrmSettingsResponse> SaveMappingsAsync(
        SaveClinicCrmMappingsRequest request, CancellationToken ct = default)
    {
        RequireCompanyId();

        var ids = request.Items.Select(i => i.AppointmentTypeId).ToHashSet();

        // Query-filter scoped: another company's service is simply not found.
        var types = await db.AppointmentTypes.Where(t => ids.Contains(t.Id) && t.IsActive).ToListAsync(ct);
        if (types.Count != ids.Count)
            throw new BusinessRuleException("APPOINTMENT_TYPE_NOT_FOUND", "One of those services no longer exists.");

        foreach (var item in request.Items)
            types.First(t => t.Id == item.AppointmentTypeId).ExternalTreatmentId = item.ExternalTreatmentId;

        await db.SaveChangesAsync(ct);
        return await BuildAsync(await LoadAsync(tracked: false, ct), ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid RequireCompanyId() =>
        tenant.CompanyId ?? throw new BusinessRuleException("TENANT_REQUIRED",
            "This endpoint must be called by a company user.");

    private async Task<ClinicCrmConnection?> LoadAsync(bool tracked, CancellationToken ct)
    {
        var query = db.ClinicCrmConnections.Where(c => c.IsActive);
        if (!tracked) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private async Task<ClinicCrmSettingsResponse> BuildAsync(ClinicCrmConnection? connection, CancellationToken ct)
    {
        var services = await db.AppointmentTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new ClinicCrmServiceMappingResponse(t.Id, t.Name, t.IsEnabled, t.ExternalTreatmentId))
            .ToListAsync(ct);

        return connection is null
            ? new ClinicCrmSettingsResponse(false, null, null, false, false, false, null, null, null, services)
            : new ClinicCrmSettingsResponse(
                true, connection.BaseUrl, connection.ClientId, !string.IsNullOrEmpty(connection.ClientSecret),
                connection.IsEnabled, connection.NotifyOnMessages, connection.LastSuccessAt, connection.LastError, connection.LastErrorAt, services);
    }

    private async Task<ClinicCrmTestResponse> RecordAsync(
        ClinicCrmConnection connection, ClinicCrmTestResponse result, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (result.Ok)
        {
            connection.LastSuccessAt = now;
            connection.LastError = null;
            connection.LastErrorAt = null;
        }
        else
        {
            connection.LastError = result.Message.Length <= 500 ? result.Message : result.Message[..500];
            connection.LastErrorAt = now;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static ClinicCrmTestResponse Failed<T>(ClinicCrmResult<T> result) => new(
        false,
        result.Outcome switch
        {
            ClinicCrmOutcome.NotAuthorised => result.Message ?? "The client ID or secret was not accepted.",
            ClinicCrmOutcome.Unavailable => result.Message ?? "The clinic's system could not be reached.",
            _ => result.Message ?? $"The clinic's system answered: {result.Outcome}.",
        },
        []);

    private static ClinicCrmEndpoint ToEndpoint(ClinicCrmConnection c) =>
        new(c.Id, c.BaseUrl, c.ClientId, c.ClientSecret);

    private static IReadOnlyList<ClinicCrmTreatmentResponse> Map(IReadOnlyList<ClinicCrmTreatment>? treatments) =>
        [.. (treatments ?? []).Select(t => new ClinicCrmTreatmentResponse(t.Id, t.Name, t.Description))];
}
