using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog.Events;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public partial class LogsTabViewModel : ViewModelBase
{
    private readonly LogService _logService;

    // Для обратной совместимости, если кто-то обращается к сырому списку
    public ObservableCollection<string> ListBoxLogs => _logService.Logs;

    // Основная коллекция отфильтрованных логов для UI
    public ObservableCollection<LogEntry> FilteredLogs { get; } = new();

    [ObservableProperty]
    private bool _filterTwitch = true;

    [ObservableProperty]
    private bool _filterGoodGame = true;

    [ObservableProperty]
    private bool _filterKick = true;

    [ObservableProperty]
    private bool _filterBluestar = true;

    [ObservableProperty]
    private bool _filterSystem = true;

    [ObservableProperty]
    private bool _filterOnlyImportant;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public LogsTabViewModel()
    {
        _logService = LoggingService.LogService;

        // Первичное наполнение
        RebuildFilteredLogs();

        // Подписка на добавление новых логов
        _logService.LogAdded += OnLogAdded;
        _logService.LogsCleared += OnLogsCleared;
    }

    private void OnLogAdded(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            HandleNewLogEntry(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => HandleNewLogEntry(entry));
        }
    }

    private void HandleNewLogEntry(LogEntry entry)
    {
        if (MatchesFilter(entry))
        {
            FilteredLogs.Insert(0, entry);
            while (FilteredLogs.Count > 1000)
            {
                FilteredLogs.RemoveAt(FilteredLogs.Count - 1);
            }
        }
        UpdateStatusText();
    }

    private void OnLogsCleared()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            FilteredLogs.Clear();
            UpdateStatusText();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                FilteredLogs.Clear();
                UpdateStatusText();
            });
        }
    }

    private bool MatchesFilter(LogEntry entry)
    {
        if (FilterOnlyImportant && entry.Level < LogEventLevel.Warning)
        {
            return false;
        }

        return entry.Service switch
        {
            LogSourceService.Twitch => FilterTwitch,
            LogSourceService.GoodGame => FilterGoodGame,
            LogSourceService.Kick => FilterKick,
            LogSourceService.Bluestar => FilterBluestar,
            _ => FilterSystem
        };
    }

    partial void OnFilterTwitchChanged(bool value) => RebuildFilteredLogs();
    partial void OnFilterGoodGameChanged(bool value) => RebuildFilteredLogs();
    partial void OnFilterKickChanged(bool value) => RebuildFilteredLogs();
    partial void OnFilterBluestarChanged(bool value) => RebuildFilteredLogs();
    partial void OnFilterSystemChanged(bool value) => RebuildFilteredLogs();
    partial void OnFilterOnlyImportantChanged(bool value) => RebuildFilteredLogs();

    private void RebuildFilteredLogs()
    {
        FilteredLogs.Clear();
        foreach (var entry in _logService.AllEntries)
        {
            if (MatchesFilter(entry))
            {
                FilteredLogs.Add(entry);
            }
        }
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        StatusText = $"Показано: {FilteredLogs.Count} / {_logService.AllEntries.Count}";
    }

    [RelayCommand]
    private void ToggleAllFilters()
    {
        bool target = !(FilterTwitch && FilterGoodGame && FilterKick && FilterBluestar && FilterSystem);
        FilterTwitch = target;
        FilterGoodGame = target;
        FilterKick = target;
        FilterBluestar = target;
        FilterSystem = target;
        RebuildFilteredLogs();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logService.Clear();
    }

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

    // Алиасы для точного совпадения с привязками XAML
    public IRelayCommand OpenLogsFolder => DoOpenLogsFolderCommand;
    public IRelayCommand OpenSettingsFolder => DoOpenSettingsFolderCommand;
    public IRelayCommand ButtonClearLogs => ClearLogsCommand;
    public IRelayCommand ButtonToggleAllFilters => ToggleAllFiltersCommand;
}
