using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public class LogsTabViewModel : INotifyPropertyChanged
{
    public ICommand OpenLogsFolder { get; }
    public ICommand OpenSettingsFolder { get; }

    public LogsTabViewModel()
    {
        OpenLogsFolder = new RelayCommand(LoggingService.OpenLogFolder);

        OpenSettingsFolder = new RelayCommand(DirectoryService.OpenAppDataFolder);
    }

    public ObservableCollection<string> ListBoxLogs =>
    LoggingService.LogService.Logs;
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(
    [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

}