using Serilog.Core;
using Serilog.Events;

namespace wa_api.Features.Admin.Logging;

public sealed class InMemoryLogSink(InMemoryLogBuffer buffer) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        buffer.Add(new LogEntry(
            logEvent.Timestamp.UtcDateTime,
            logEvent.Level.ToString(),
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString()
        ));
    }
}
