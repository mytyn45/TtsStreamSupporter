using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public partial class LogsTabViewModel : ViewModelBase
{
    public ObservableCollection<string> ListBoxLogs => LoggingService.LogService.Logs;

    [RelayCommand]
    private void DoOpenLogsFolder()
    {
        LoggingService.OpenLogFolder();
    }

    [RelayCommand]
    private void DoOpenSettingsFolder()
    {
        DirectoryService.OpenAppDataFolder();
    }

    // Алиасы для точного совпадения с существующими привязками XAML
    public IRelayCommand OpenLogsFolder => DoOpenLogsFolderCommand;
    public IRelayCommand OpenSettingsFolder => DoOpenSettingsFolderCommand;
}
