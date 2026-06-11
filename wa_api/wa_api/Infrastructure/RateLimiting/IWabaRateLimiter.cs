namespace wa_api.Infrastructure.RateLimiting;

public interface IWabaRateLimiter
{
    /// <summary>
    /// Checks whether the given phone-number id is within its send quota for the current
    /// one-minute window. Throws <see cref="wa_api.Common.Errors.RateLimitException"/> when
    /// the limit is exceeded; returns normally when the send is allowed.
    /// </summary>
    Task CheckAsync(string phoneNumberId, CancellationToken ct = default);
}
