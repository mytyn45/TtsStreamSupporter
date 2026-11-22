using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace TtsStreamSupporter.Services;

public class LogService
{
    private const int MaxLogsCount = 1000;

    public ObservableCollection<LogEntry> AllEntries { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public event Action<LogEntry>? LogAdded;
    public event Action? LogsCleared;

    public void Add(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddInternal(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddInternal(entry));
        }
    }

    public void Add(string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Service = LogEntry.DetectService(message)
        };
        Add(entry);
    }

    private void AddInternal(LogEntry entry)
    {
        AllEntries.Insert(0, entry);
        Logs.Insert(0, $"[{entry.TimeText}] [{entry.Level}] {entry.Message}");

        while (AllEntries.Count > MaxLogsCount)
        {
            AllEntries.RemoveAt(AllEntries.Count - 1);
        }

        while (Logs.Count > MaxLogsCount)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        LogAdded?.Invoke(entry);
    }

    public void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AllEntries.Clear();
            Logs.Clear();
            LogsCleared?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                AllEntries.Clear();
                Logs.Clear();
                LogsCleared?.Invoke();
            });
        }
    }
}
