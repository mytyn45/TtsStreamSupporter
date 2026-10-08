using Avalonia;
using Serilog;
using System;
using Microsoft.Extensions.DependencyInjection;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;
using System.Threading.Tasks;

namespace TtsStreamSupporter;

class Program
{
    public static ServiceProvider Services { get; private set; } = null!;
    [STAThread]
    public static void Main(string[] args)
    {
        DirectoryService.EnsureAppStructureExists();

        LoggingService.Initialize();

        Log.Information("Приложение запускается");

        Services = ServiceCollectionExtensions.BuildServices();

        // Загружаем настройки
        Services.GetRequiredService<SettingsService>().Load();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Критическая ошибка приложения");
        }
        finally
        {
            Log.Information("Приложение завершает работу");
            Log.CloseAndFlush();
        }
    }



    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
