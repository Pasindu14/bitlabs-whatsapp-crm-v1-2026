namespace wa_api.Features.ClinicCrm;

/// <summary>Settings shared by every clinic CRM connection, bound from <c>ClinicCrm</c>.</summary>
public sealed class ClinicCrmOptions
{
    public const string SectionName = "ClinicCrm";

    /// <summary>
    /// Per call. Short on purpose: the bot calls the CRM while the customer waits on the other end of a
    /// WhatsApp thread, and a booking that cannot reach the CRM is queued rather than lost.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// The zone the CRM reads every date and time in. The API document fixes it to the clinic's local time
    /// (Asia/Dubai); every value we send is converted into it, whatever zone the company runs in.
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Dubai";

    /// <summary>A token is renewed this long before it expires, so a call never carries a dying one.</summary>
    public int TokenRefreshMarginSeconds { get; set; } = 60;
}
