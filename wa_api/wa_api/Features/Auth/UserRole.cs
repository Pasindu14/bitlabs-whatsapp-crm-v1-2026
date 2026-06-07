namespace wa_api.Features.Auth;

/// <summary>
/// Platform roles. <see cref="SuperAdmin"/> is platform-level (CompanyId == null);
/// every other role is tenant-scoped (CompanyId required).
/// Stored as a string column so the DB stays readable and the enum can grow safely.
/// </summary>
public enum UserRole
{
    SuperAdmin = 0,   // platform owner — creates companies, WABA connections, company admins
    CompanyAdmin = 1, // manages their own company
    Agent = 2         // operates within a company (chats, contacts…)
}
