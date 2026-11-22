using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Calendar.Entities;

/// <summary>
/// One opening window on one weekday, in the company's LOCAL wall-clock time.
/// <para>
/// Deliberately NOT stored in UTC. "We open at 09:00 on Mondays" is a statement about local time that
/// stays true across a daylight-saving transition; storing 03:30Z would silently shift the shop's
/// opening hour twice a year. The availability calculator projects these to UTC per concrete date,
/// which is the only point where an offset can be resolved correctly.
/// </para>
/// <para>
/// Multiple rows per weekday are allowed and are how a lunch break is expressed — 09:00–12:00 and
/// 13:00–17:00 are two rows, and the gap between them is simply not bookable.
/// </para>
/// </summary>
public class BusinessHours : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context on the HTTP path.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Weekday this window applies to. Stored as text so appending never needs a backfill.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Local opening time (inclusive).</summary>
    public TimeOnly StartLocal { get; set; }

    /// <summary>
    /// Local closing time (exclusive). Must be strictly after <see cref="StartLocal"/> — a window that
    /// crosses midnight is expressed as two rows on two weekdays, so the calculator never has to reason
    /// about a window that spans a date boundary.
    /// </summary>
    public TimeOnly EndLocal { get; set; }
}
