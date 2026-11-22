using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Calendar.Entities;

/// <summary>
/// A bookable service the company offers — "Consultation (30 min)", "Property viewing (1 hr)".
/// Tenant-scoped via <see cref="CompanyId"/> and isolated by the global query filter, exactly like
/// every other tenant entity.
/// <para>
/// This is the unit the availability engine works in: <see cref="DurationMinutes"/> decides how long a
/// slot is, <see cref="SlotIntervalMinutes"/> decides how often one may start, and the two buffers
/// reserve dead time around the appointment without showing it to the customer as part of the booking.
/// </para>
/// </summary>
public class AppointmentType : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context on the HTTP path; stamped
    /// explicitly by any background job (jobs have no tenant context).</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Customer-facing name. Unique per company.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional blurb shown alongside the name when offering slots.</summary>
    public string? Description { get; set; }

    /// <summary>How long the appointment itself runs. This is what the customer is told.</summary>
    public int DurationMinutes { get; set; } = 30;

    /// <summary>
    /// Granularity of slot START times within a business-hours window, in minutes. A 30-minute
    /// appointment with a 15-minute interval offers 09:00, 09:15, 09:30 … rather than only 09:00 and
    /// 09:30. Defaults to the duration, which yields back-to-back slots.
    /// </summary>
    public int SlotIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Dead time reserved BEFORE the appointment (prep, travel). Blocks the calendar but is invisible
    /// to the customer — they are told the appointment time, not the buffer.
    /// </summary>
    public int BufferBeforeMinutes { get; set; }

    /// <summary>Dead time reserved AFTER the appointment (write-up, turnaround, cleaning).</summary>
    public int BufferAfterMinutes { get; set; }

    /// <summary>
    /// How far ahead of NOW the earliest bookable slot must be, in minutes. Stops a customer booking a
    /// slot starting in four minutes, which nobody can honour. 0 disables the check.
    /// </summary>
    public int MinimumNoticeMinutes { get; set; } = 60;

    /// <summary>
    /// How many days ahead slots are offered. Bounds both the calculator's work and how far into an
    /// unknowable future the company commits.
    /// </summary>
    public int BookingHorizonDays { get; set; } = 30;

    /// <summary>Optional price, for display only — this module does not take payment.</summary>
    public decimal? Price { get; set; }

    /// <summary>Lets an operator retire a type without deleting it (existing appointments keep their
    /// reference). Distinct from <see cref="BaseEntity.IsActive"/>, the platform-wide soft-delete flag.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The clinic CRM treatment this service books as (plan 014), chosen by the admin from the CRM's own
    /// list. While a CRM connection is on, a service without one cannot be booked by the bot — names are
    /// never matched across the two systems by guesswork.
    /// </summary>
    public int? ExternalTreatmentId { get; set; }
}
