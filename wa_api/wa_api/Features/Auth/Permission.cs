using System.Collections.Frozen;

namespace wa_api.Features.Auth;

/// <summary>
/// The fixed permission catalog — the single source of truth for fine-grained
/// capabilities a CompanyAdmin can grant to <see cref="UserRole.Agent"/> users.
/// <para>
/// These are stored per-user as a <c>text[]</c> column on <see cref="User"/> and surfaced
/// as a JWT claim for UI gating. SuperAdmin and CompanyAdmin are all-access by ROLE, so they
/// carry an empty permission set — enforcement (Phase 2) bypasses the check for those roles.
/// </para>
/// <para>
/// KEYS ARE A CONTRACT: never rename or reuse a key once shipped (it would silently change
/// what existing users can do). To add a capability, add a new constant here and to the
/// frontend catalog — no migration needed (the column already exists).
/// </para>
/// </summary>
public static class Permission
{
    public const string LiveMessage = "live_message";
    public const string TokenPurchase = "token_purchase";
    public const string ManageTemplate = "manage_template";
    public const string ScheduleCampaign = "schedule_campaign";
    public const string ContactList = "contact_list";
    public const string ManageUser = "manage_user";
    public const string Analytics = "analytics";

    /// <summary>Every known permission key — used to validate incoming grants.</summary>
    public static readonly FrozenSet<string> All = new[]
    {
        LiveMessage,
        TokenPurchase,
        ManageTemplate,
        ScheduleCampaign,
        ContactList,
        ManageUser,
        Analytics,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True when every key in <paramref name="keys"/> is part of the catalog.</summary>
    public static bool AreAllValid(IEnumerable<string> keys) => keys.All(All.Contains);
}
