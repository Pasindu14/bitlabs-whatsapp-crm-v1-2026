namespace wa_api.Infrastructure.Locking;

public interface IDistributedLockService
{
    /// <summary>
    /// Tries to acquire a mutually-exclusive lock on <paramref name="resource"/>.
    /// Returns a handle to release on dispose, or <c>null</c> if the lock is held elsewhere.
    /// </summary>
    Task<IAsyncDisposable?> AcquireAsync(string resource, CancellationToken ct = default);
}
