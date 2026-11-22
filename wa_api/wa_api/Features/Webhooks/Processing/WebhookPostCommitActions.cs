namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Side effects a webhook handler wants to run only once its batch has COMMITTED — today, the realtime
/// pushes. Handlers never call SaveChanges (<see cref="WebhookProcessingJob"/> commits once), so a push sent
/// from inside a handler reaches the browser before the data exists: the inbox refetches on the event, reads
/// the pre-commit state, caches it, and never hears about the change again. Scoped to the job's DI scope.
/// </summary>
public sealed class WebhookPostCommitActions(ILogger<WebhookPostCommitActions> logger)
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    /// <summary>Queues <paramref name="action"/> to run after the batch commits.</summary>
    public void Add(Func<CancellationToken, Task> action) => _actions.Add(action);

    /// <summary>
    /// Drops everything queued. Called at the start of each execution-strategy attempt — a retried attempt
    /// re-runs the handlers, which queue their actions again.
    /// </summary>
    public void Clear() => _actions.Clear();

    /// <summary>
    /// Runs the queued actions in order, then clears them. Best-effort: the batch is already committed, so a
    /// failed action is logged and the rest still run — it must never turn a processed webhook into a retry.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        foreach (var action in _actions)
        {
            try
            {
                await action(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Post-commit webhook action failed — non-fatal.");
            }
        }

        _actions.Clear();
    }
}
