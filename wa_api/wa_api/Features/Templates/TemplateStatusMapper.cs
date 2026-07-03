using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates;

/// <summary>
/// Shared mapping of Meta <c>message_template</c> statuses onto our entity — used by the submit
/// path, the on-demand refresh, and the recurring poll job so the rules stay in one place.
/// </summary>
public static class TemplateStatusMapper
{
    /// <summary>Maps a Meta status string to our enum (null when unrecognized → leave as-is).</summary>
    public static TemplateStatus? Parse(string? metaStatus) => metaStatus?.ToUpperInvariant() switch
    {
        "APPROVED" => TemplateStatus.Approved,
        "PENDING" => TemplateStatus.Pending,
        "REJECTED" => TemplateStatus.Rejected,
        "PAUSED" => TemplateStatus.Paused,
        "DISABLED" => TemplateStatus.Disabled,
        _ => null,
    };

    /// <summary>Applies a polled status onto the template: maps status, captures/clears the
    /// rejection reason, stamps <c>ApprovedAt</c> on first approval, and stamps <c>LastSyncedAt</c>.</summary>
    public static void Apply(Template t, MetaTemplateStatus status, DateTime now)
    {
        if (Parse(status.Status) is { } mapped)
            t.Status = mapped;
        t.RejectionReason = t.Status == TemplateStatus.Rejected ? status.RejectionReason : null;
        // Record when Meta first approved it. Set once and never overwritten, so a later
        // Paused→Approved cycle keeps the original approval time.
        if (t.Status == TemplateStatus.Approved && t.ApprovedAt is null)
            t.ApprovedAt = now;
        t.LastSyncedAt = now;
    }
}
