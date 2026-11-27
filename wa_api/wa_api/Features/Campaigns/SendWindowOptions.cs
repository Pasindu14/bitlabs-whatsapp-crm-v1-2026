namespace wa_api.Features.Campaigns;

/// <summary>
/// Per-company exemptions from the UAE quiet-hours <see cref="SendWindow"/>.
/// <para>
/// Meant for a short-lived account such as a Meta app-review tenant, whose reviewers work in other
/// time zones and would otherwise hit OUTSIDE_SEND_WINDOW. Real UAE customers must never be listed.
/// Config only (no DB column), so adding or removing a company is an env change plus restart:
/// <c>Campaigns__SendWindow__ExemptCompanyIds__0=&lt;company-guid&gt;</c>.
/// </para>
/// </summary>
public sealed class SendWindowOptions
{
    public const string SectionName = "Campaigns:SendWindow";

    public List<Guid> ExemptCompanyIds { get; set; } = [];

    public bool IsExempt(Guid? companyId) =>
        companyId is { } id && ExemptCompanyIds.Contains(id);
}
