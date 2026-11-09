using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.ViewModels;

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
            var mainWindow = new MainWindow
            {
                DataContext = Program.Services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.MainWindow = mainWindow;
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
