using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serilog;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();

        // Фоновая загрузка движка Piper без блокировки запуска программы
        _ = Task.Run(async () =>
        {
            try
            {
                // Небольшая задержка, чтобы приложение полностью запустилось и отобразило UI
                await Task.Delay(3000);
                await PiperVoicesData.EnsureEngineDownloadedAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при фоновой загрузке движка Piper");
            }
        });
    }
}