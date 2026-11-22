using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;

namespace wa_api.Features.Calendar.Entities;

/// <summary>
/// Lifecycle of a booking. Stored as text (like every other enum here) so appending values never needs
/// a migration of existing rows.
/// <para>
/// Every value except <see cref="Cancelled"/> OCCUPIES the slot — including the terminal
/// <see cref="Completed"/> and <see cref="NoShow"/>, because a past appointment still happened and must
/// not be retroactively double-booked. That is exactly the predicate the database's overlap constraint
/// filters on.
/// </para>
/// </summary>
public enum AppointmentStatus
{
    /// <summary>Slot is held but not yet confirmed — a bot conversation still in flight. Expires via
    /// <see cref="Appointment.HoldExpiresAt"/>.</summary>
    Pending,

    /// <summary>Booked and agreed.</summary>
    Confirmed,

    /// <summary>Called off. The ONLY status that frees the slot.</summary>
    Cancelled,

    /// <summary>Happened.</summary>
    Completed,

    /// <summary>Customer did not turn up.</summary>
    NoShow,
}

/// <summary>
/// Where a booking stands with the clinic's CRM (plan 014). Stored as text.
/// </summary>
public enum ExternalSyncStatus
{
    /// <summary>The company has no CRM connection, or the booking predates it. Ours alone.</summary>
    NotApplicable,

    /// <summary>The CRM accepted it; <see cref="Appointment.ExternalBookingId"/> is its booking.</summary>
    Synced,

    /// <summary>The CRM could not be reached; a background job is still trying to push it.</summary>
    Pending,

    /// <summary>The CRM refused it, or every retry failed. Staff have been told; a person must rebook.</summary>
    Failed,

    /// <summary>
    /// Cancelled here, and the cancellation flagged on the CRM booking. The CRM's API cannot cancel, so a
    /// person at the clinic still has to cancel it there.
    /// </summary>
    CancelRequested,
}

/// <summary>Where the booking came from. Stored as text.</summary>
public enum AppointmentSource
{
    /// <summary>Created by the chatbot on conversion.</summary>
    Bot,

    /// <summary>Created by a human agent from the inbox, on the customer's behalf.</summary>
    Agent,

    /// <summary>Created by an admin directly on the calendar screen.</summary>
    Admin,
}

/// <summary>
/// One booking on the company's single shared calendar. Tenant-scoped via <see cref="CompanyId"/> and
/// isolated by the global query filter, exactly like <see cref="Conversation"/>.
/// <para>
/// <b>Two time ranges, and the difference matters.</b> <see cref="StartUtc"/>/<see cref="EndUtc"/> is
/// the appointment the customer is told about. <see cref="BlockStartUtc"/>/<see cref="BlockEndUtc"/> is
/// that range widened by the appointment type's before/after buffers, and it is what actually occupies
/// the calendar. Collapsing the two would let a booking with a 15-minute turnaround buffer sit flush
/// against the next one, silently eating the buffer that was configured precisely to prevent that.
/// </para>
/// <para>
/// <b>Overlap is prevented by the database, not by application code.</b> A Postgres exclusion constraint
/// on <c>(CompanyId, tstzrange(BlockStartUtc, BlockEndUtc))</c> filtered to non-cancelled rows makes a
/// double booking physically unrepresentable. Two customers confirming the same slot concurrently
/// through the bot is a real race that a read-then-write check cannot win; here the loser simply gets a
/// unique-violation, which the service turns into a clean SLOT_TAKEN conflict.
/// </para>
/// <para>All <see cref="DateTime"/> values are UTC.</para>
/// </summary>
public class Appointment : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context on the HTTP path; stamped
    /// explicitly by the bot's booking job (jobs have no tenant context).</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Who the appointment is with.</summary>
    public Guid ContactId { get; set; }

    /// <summary>What was booked.</summary>
    public Guid AppointmentTypeId { get; set; }

    /// <summary>
    /// The conversation this booking came out of. Null for an admin-created booking with no chat behind
    /// it. Set by the bot so the thread and the appointment stay linked in both directions.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>UTC start of the appointment as told to the customer (inclusive).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>UTC end of the appointment as told to the customer (exclusive).</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// UTC start of the range this booking OCCUPIES — <see cref="StartUtc"/> minus the type's before-buffer.
    /// Equal to <see cref="StartUtc"/> when the buffer is zero. This is what the overlap constraint sees.
    /// </summary>
    public DateTime BlockStartUtc { get; set; }

    /// <summary>UTC end of the occupied range — <see cref="EndUtc"/> plus the type's after-buffer.</summary>
    public DateTime BlockEndUtc { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;

    public AppointmentSource Source { get; set; } = AppointmentSource.Admin;

    /// <summary>Free-text note from whoever booked it.</summary>
    public string? Notes { get; set; }

    /// <summary>The user who created it. Null when the bot did.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// UTC deadline for a <see cref="AppointmentStatus.Pending"/> hold. While the bot is still walking a
    /// customer through confirmation the slot must not be sold twice, so it is taken immediately as a
    /// hold; if the customer goes quiet, the hold is cancelled and the slot returns to the pool. Null
    /// once confirmed — and null for anything that was never a hold.
    /// <para>
    /// NOTE: nothing creates a hold yet. Holds are taken by the chatbot's booking step, and the sweeper
    /// that expires them ships with it — this column and the index on it exist so that phase does not
    /// need a migration. Every appointment written today is <see cref="AppointmentStatus.Confirmed"/>.
    /// </para>
    /// <para>
    /// The expiry deliberately does NOT appear in the overlap constraint: a constraint predicate must be
    /// immutable, and "expired" changes with wall-clock time. Sweeping to <c>Cancelled</c> is what
    /// actually frees the slot, which keeps one rule ("cancelled means free") rather than two.
    /// </para>
    /// </summary>
    public DateTime? HoldExpiresAt { get; set; }

    /// <summary>UTC when the booking was cancelled. Null unless <see cref="Status"/> is Cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>Why it was cancelled — shown in history.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// UTC when the reminder message was sent. Null until then, and the reminder job's idempotency key:
    /// a job that crashes after sending must not send twice on retry.
    /// </summary>
    public DateTime? ReminderSentAt { get; set; }

    // ── Clinic CRM mirror (plan 014) ──────────────────────────────────────────
    // For a company connected to its clinic's CRM, the CRM decides and this row mirrors it.

    /// <summary>The CRM's booking id. Changes on every move — the CRM books the new time under a new id.</summary>
    public int? ExternalBookingId { get; set; }

    /// <summary>The CRM's id for the customer.</summary>
    public int? ExternalCustomerId { get; set; }

    public ExternalSyncStatus ExternalSyncStatus { get; set; } = ExternalSyncStatus.NotApplicable;

    /// <summary>Why the last push failed, for staff.</summary>
    public string? ExternalSyncError { get; set; }

    /// <summary>UTC when the CRM last accepted this booking.</summary>
    public DateTime? ExternalSyncedAt { get; set; }

    public Contact Contact { get; set; } = null!;
    public AppointmentType AppointmentType { get; set; } = null!;
    public Conversation? Conversation { get; set; }
}
