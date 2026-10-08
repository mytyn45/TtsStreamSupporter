using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace TtsStreamSupporter.Services;

public class LogService
{
    public ObservableCollection<string> Logs { get; } = new();
    public void Add(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Logs.Add(message);

            // Ограничиваем количество логов
            if (Logs.Count > 100)
                Logs.RemoveAt(0);
        });
    }
}