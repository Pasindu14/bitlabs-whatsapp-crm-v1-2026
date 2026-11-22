using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Calendar.Entities;

/// <summary>
/// A span during which nothing may be booked, regardless of what <see cref="BusinessHours"/> says —
/// a public holiday, annual leave, a closed afternoon.
/// <para>
/// Stored in UTC (unlike <see cref="BusinessHours"/>) because a blackout is a concrete interval on the
/// timeline rather than a recurring wall-clock rule. The API accepts local dates/times and converts on
/// the way in, using the company's timezone.
/// </para>
/// </summary>
public class CalendarBlackout : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context on the HTTP path.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>UTC start of the blocked span (inclusive).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>UTC end of the blocked span (exclusive).</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Operator-facing label — "Christmas Day", "Team offsite". Shown on the admin calendar.</summary>
    public string Reason { get; set; } = string.Empty;
}
