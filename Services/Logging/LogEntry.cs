using System;
using Avalonia;
using Avalonia.Media;
using Serilog.Events;

namespace TtsStreamSupporter.Services;

public enum LogSourceService
{
    General,
    Twitch,
    GoodGame,
    Kick,
    Bluestar
}

public class LogEntry
{
    // =========================================================
    // БЕЙДЖИ СЕРВИСОВ
    // =========================================================

    // Twitch - фиолетовый
    private static readonly IBrush TwitchBackground = Brush.Parse("#9146FF");
    private static readonly IBrush TwitchForeground = Brushes.White;

    // GoodGame - #233056
    private static readonly IBrush GoodGameBackground = Brush.Parse("#233056");
    private static readonly IBrush GoodGameForeground = Brushes.White;

    // Kick - ярко-зелёный (#00E701 / #53FC18)
    private static readonly IBrush KickBackground = Brush.Parse("#00E701");
    private static readonly IBrush KickForeground = Brushes.Black;

    // Bluestar - половина синего, половина оранжевого (LinearGradient с резким переходом 50/50)
    private static readonly IBrush BluestarBackground = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#1E88E5"), 0.0),
            new GradientStop(Color.Parse("#1E88E5"), 0.5),
            new GradientStop(Color.Parse("#FF8C00"), 0.5),
            new GradientStop(Color.Parse("#FF8C00"), 1.0)
        }
    };
    private static readonly IBrush BluestarForeground = Brushes.White;

    // Общие / Системные
    private static readonly IBrush GeneralBackground = Brush.Parse("#6C757D");
    private static readonly IBrush GeneralForeground = Brushes.White;

    // =========================================================
    // ЦВЕТА УРОВНЕЙ ЛОГОВ (ВАЖНЫЕ = ЖЁЛТЫЙ, КРИТИЧЕСКИЕ = КРАСНЫЙ)
    // =========================================================

    // Важные (Warning) - Жёлтый / Янтарный
    private static readonly IBrush WarningRowBackground = Brush.Parse("#FFFDE7");
    private static readonly IBrush WarningRowBorderBrush = Brush.Parse("#FBC02D");
    private static readonly IBrush WarningTextForeground = Brush.Parse("#78350F");
    private static readonly IBrush WarningBadgeBackground = Brush.Parse("#F59E0B");
    private static readonly IBrush WarningBadgeForeground = Brushes.White;

    // Критические (Error / Fatal) - Красный
    private static readonly IBrush ErrorRowBackground = Brush.Parse("#FFEBEE");
    private static readonly IBrush ErrorRowBorderBrush = Brush.Parse("#EF5350");
    private static readonly IBrush ErrorTextForeground = Brush.Parse("#991B1B");
    private static readonly IBrush ErrorBadgeBackground = Brush.Parse("#DC2626");
    private static readonly IBrush ErrorBadgeForeground = Brushes.White;

    // Обычные (Information / Debug / Verbose)
    private static readonly IBrush NormalRowBackground = Brushes.Transparent;
    private static readonly IBrush NormalRowBorderBrush = Brush.Parse("#E5E7EB");
    private static readonly IBrush NormalTextForeground = Brush.Parse("#1F2937");
    private static readonly IBrush NormalBadgeBackground = Brush.Parse("#E5E7EB");
    private static readonly IBrush NormalBadgeForeground = Brush.Parse("#4B5563");

    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogEventLevel Level { get; init; } = LogEventLevel.Information;
    public LogSourceService Service { get; init; } = LogSourceService.General;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }

    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string ServiceName => Service switch
    {
        LogSourceService.Twitch => "Twitch",
        LogSourceService.GoodGame => "GoodGame",
        LogSourceService.Kick => "Kick",
        LogSourceService.Bluestar => "Bluestar",
        _ => "Система"
    };

    public string LevelText => Level switch
    {
        LogEventLevel.Fatal => "КРИТ",
        LogEventLevel.Error => "ОШИБКА",
        LogEventLevel.Warning => "ВАЖНО",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Verbose => "TRACE",
        _ => "ИНФО"
    };

    public IBrush ServiceBackground => Service switch
    {
        LogSourceService.Twitch => TwitchBackground,
        LogSourceService.GoodGame => GoodGameBackground,
        LogSourceService.Kick => KickBackground,
        LogSourceService.Bluestar => BluestarBackground,
        _ => GeneralBackground
    };

    public IBrush ServiceForeground => Service switch
    {
        LogSourceService.Kick => KickForeground,
        _ => Brushes.White
    };

    public IBrush RowBackground => Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => ErrorRowBackground,
        LogEventLevel.Warning => WarningRowBackground,
        _ => NormalRowBackground
    };

    public IBrush RowBorderBrush => Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => ErrorRowBorderBrush,
        LogEventLevel.Warning => WarningRowBorderBrush,
        _ => NormalRowBorderBrush
    };

    public IBrush TextForeground => Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => ErrorTextForeground,
        LogEventLevel.Warning => WarningTextForeground,
        _ => NormalTextForeground
    };

    public IBrush LevelBadgeBackground => Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => ErrorBadgeBackground,
        LogEventLevel.Warning => WarningBadgeBackground,
        _ => NormalBadgeBackground
    };

    public IBrush LevelBadgeForeground => Level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => ErrorBadgeForeground,
        LogEventLevel.Warning => WarningBadgeForeground,
        _ => NormalBadgeForeground
    };

    public static LogSourceService DetectService(string message, LogEvent? logEvent = null)
    {
        if (logEvent != null && logEvent.Properties.TryGetValue("Service", out var propVal))
        {
            string cleanProp = propVal.ToString().Trim('"', '\'');
            if (Enum.TryParse<LogSourceService>(cleanProp, true, out var svc))
                return svc;
        }

        if (message.Contains("[Twitch]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Twitch", StringComparison.OrdinalIgnoreCase))
        {
            return LogSourceService.Twitch;
        }

        if (message.Contains("[GoodGame]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("GoodGame", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Гудгейм", StringComparison.OrdinalIgnoreCase))
        {
            return LogSourceService.GoodGame;
        }

        if (message.Contains("[Kick]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Kick", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Pusher", StringComparison.OrdinalIgnoreCase))
        {
            return LogSourceService.Kick;
        }

        if (message.Contains("[Bluestar]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Bluestar", StringComparison.OrdinalIgnoreCase))
        {
            return LogSourceService.Bluestar;
        }

        return LogSourceService.General;
    }
}
