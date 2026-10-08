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

        _logService.Add(
            $"[{logEvent.Timestamp:HH:mm:ss}] [{logEvent.Level}] {message}");
    }
}