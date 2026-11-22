namespace wa_api.Features.Calendar;

/// <summary>
/// Resolves a stored IANA timezone id to a <see cref="TimeZoneInfo"/>.
/// <para>
/// .NET resolves IANA ids on Windows and Linux alike through ICU, so one stored value behaves the same
/// on a developer machine and in production. A stored id can still fail to resolve — a host without ICU
/// data, or an id retired by a tzdb update — and the calendar must degrade rather than 500 on every
/// availability request, so lookups fall back to UTC.
/// </para>
/// </summary>
public static class CompanyTimeZone
{
    /// <summary>Fallback when a company has not chosen a zone, or its stored id no longer resolves.</summary>
    public static readonly TimeZoneInfo Fallback = TimeZoneInfo.Utc;

    /// <summary>True when <paramref name="id"/> names a zone this host knows. Used to validate input
    /// BEFORE storing it, which is the only place a bad id can still be rejected cleanly.</summary>
    public static bool IsValid(string? id) => TryResolve(id, out _);

    /// <summary>Resolves <paramref name="id"/>, or returns false without throwing.</summary>
    public static bool TryResolve(string? id, out TimeZoneInfo timeZone)
    {
        timeZone = Fallback;
        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>Resolves <paramref name="id"/>, falling back to UTC when it cannot be resolved.</summary>
    public static TimeZoneInfo ResolveOrFallback(string? id) => TryResolve(id, out var tz) ? tz : Fallback;

    /// <summary>
    /// Normalises a <see cref="DateTime"/> arriving from the wire to UTC. The API contract is "all
    /// timestamps are UTC", but JSON without a trailing Z deserialises as <see cref="DateTimeKind.Unspecified"/>,
    /// and Npgsql rejects a non-UTC kind for a <c>timestamptz</c> column — so a naive value is taken at
    /// its word rather than being silently shifted by the server's local offset.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Today's date in the company's zone — the anchor for a default availability window.</summary>
    public static DateOnly TodayIn(TimeZoneInfo timeZone, DateTime nowUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));

    /// <summary>
    /// Converts a local wall-clock time to UTC WITHOUT throwing when that time does not exist.
    /// <para>
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws on a time inside a
    /// spring-forward gap. Most zones shift at 01:00–03:00, but several (America/Santiago, Asia/Beirut,
    /// Cuba) shift at midnight — so converting "the start of this local day" would throw a handful of
    /// times a year in those zones and take the availability endpoint down with it.
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> never throws and, for a gap time, returns the
    /// offset in force before the transition.
    /// </para>
    /// <para>
    /// Use this ONLY for range boundaries, where landing an hour either side of a transition is
    /// harmless. Slot starts are converted by the calculator, which deliberately skips gap times
    /// instead of coercing them into an instant that does not exist.
    /// </para>
    /// </summary>
    public static DateTime DayBoundaryToUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var local = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(local - timeZone.GetUtcOffset(local), DateTimeKind.Utc);
    }
}
