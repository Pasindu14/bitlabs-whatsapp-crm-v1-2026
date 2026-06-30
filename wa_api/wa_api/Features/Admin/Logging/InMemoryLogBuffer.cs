using System.Collections.Concurrent;

namespace wa_api.Features.Admin.Logging;

/// <summary>
/// Singleton ring-buffer of the last <see cref="Capacity"/> log entries.
/// Created as a static instance so it can be injected into the Serilog sink
/// before the DI container is built, then registered in DI for controller use.
/// </summary>
public sealed class InMemoryLogBuffer
{
    public static readonly InMemoryLogBuffer Instance = new();

    private const int Capacity = 1000;
    private readonly ConcurrentQueue<LogEntry> _queue = new();

    public void Add(LogEntry entry)
    {
        _queue.Enqueue(entry);
        while (_queue.Count > Capacity)
            _queue.TryDequeue(out _);
    }

    public IReadOnlyList<LogEntry> GetRecent(int count)
    {
        var all = _queue.ToArray();
        return all.Length <= count ? all : all[(all.Length - count)..];
    }

    public IReadOnlyList<LogEntry> GetSince(DateTime since)
        => _queue.Where(e => e.Timestamp > since).ToList();
}
