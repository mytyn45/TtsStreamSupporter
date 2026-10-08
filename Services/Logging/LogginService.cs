using System;
using System.IO;
using System.Diagnostics;
using Serilog;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public static class LoggingService
{
    public static LogService LogService { get; } = new();

    private static readonly string LogDirectory =
        Path.Combine(DirectoryService.AppDataFolder, "logs");

    public static void Initialize()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(LogDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Sink(new SerilogSink(LogService))
            .CreateLogger();
    }

    public static void OpenLogFolder()
    {
        if (!Directory.Exists(LogDirectory))
        {
            Directory.CreateDirectory(LogDirectory);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{LogDirectory}\"",
            UseShellExecute = true
        });
    }
}