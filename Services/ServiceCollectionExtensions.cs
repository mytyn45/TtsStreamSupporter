using Microsoft.Extensions.DependencyInjection;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter;

public static class ServiceCollectionExtensions
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();

        services.AddSingleton<ConnectionsTabViewModel>();
        services.AddSingleton<TtsTabViewModel>();
        services.AddSingleton<LogsTabViewModel>();
        services.AddSingleton<IndividualVoicesTabViewModel>();
        services.AddSingleton<SpeechPlayerService>();
        services.AddSingleton<NttsTabViewModel>();
        services.AddSingleton<TtsMessageProcessing>();
        services.AddSingleton<MessageQueue>();
        services.AddSingleton<TwitchConnection>();
        services.AddSingleton<GoodgameConnection>();
        services.AddSingleton<NttsSpeakerLookupService>();
        services.AddSingleton<IndividualVoiceService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<BluestarConnection>();
        services.AddSingleton<AppServices>();

        return services.BuildServiceProvider();
    }
}