using Microsoft.Extensions.DependencyInjection;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter;

public static class ServiceCollectionExtensions
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Сервисы ядра и настроек
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IVoiceDiscoveryService, VoiceDiscoveryService>();
        services.AddSingleton<SpeechPlayerService>();
        services.AddSingleton<MessageQueue>();
        services.AddSingleton<NttsSpeakerLookupService>();
        services.AddSingleton<IndividualVoiceService>();
        services.AddSingleton<TtsMessageProcessing>();

        // Сетевые подключения
        services.AddSingleton<TwitchConnection>();
        services.AddSingleton<GoodgameConnection>();
        services.AddSingleton<KickConnection>();
        services.AddSingleton<BluestarConnection>();
        services.AddSingleton<IChatRouterService, ChatRouterService>();

        // ViewModels
        services.AddSingleton<ConnectionsTabViewModel>();
        services.AddSingleton<TtsTabViewModel>();
        services.AddSingleton<LogsTabViewModel>();
        services.AddSingleton<IndividualVoicesTabViewModel>();
        services.AddSingleton<NttsTabViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
