using Serilog.Core;
using Serilog.Events;

namespace TtsStreamSupporter.Services;

public class SerilogSink : ILogEventSink
{
    private readonly LogService _logService;

    public SerilogSink(LogService logService)
    {
        _logService = logService;
    }

    public void Emit(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage();

        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.LocalDateTime,
            Level = logEvent.Level,
            Message = message,
            Exception = logEvent.Exception,
            Service = LogEntry.DetectService(message, logEvent)
        };

        _logService.Add(entry);
    }
}
