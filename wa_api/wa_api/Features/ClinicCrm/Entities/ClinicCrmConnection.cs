using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.ClinicCrm.Entities;

/// <summary>
/// A company's link to its clinic's CRM through the CRM's WhatsApp BOT API (plan 014). One live row per
/// company; a company without one books on our own calendar exactly as before.
/// <para>
/// While it is enabled the CRM is the source of truth: the bot offers the CRM's free times and books
/// there first, and our <c>Appointment</c> row is only a mirror of what the CRM accepted.
/// </para>
/// </summary>
public class ClinicCrmConnection : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>The API address the clinic's admin gave us, e.g. <c>http://169.58.92.105:3110/api/bot</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The connection's client id (<c>gdc_…</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret (<c>gds_…</c>). Encrypted at rest by the same converter as the WABA secrets, and
    /// never returned by the API once saved.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Off = behave as if there were no connection, without losing the credentials.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Tell the CRM (POST /messages) each time a customer sends a real message, so the clinic's staff see the
    /// chat is live. Off by default; only counts while <see cref="IsEnabled"/> is on.
    /// </summary>
    public bool NotifyOnMessages { get; set; }

    /// <summary>When a call to the CRM last succeeded. Shown on the settings screen.</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>The last failure, in words a clinic admin can act on.</summary>
    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }
}
