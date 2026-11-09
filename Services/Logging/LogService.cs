using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace TtsStreamSupporter.Services;

public class LogService
{
    public ObservableCollection<string> Logs { get; } = new();

    public void Add(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddInternal(message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddInternal(message));
        }
    }

    private void AddInternal(string message)
    {
        Logs.Insert(0, message);

        // Ограничиваем количество логов, удаляя старейшие с конца списка
        while (Logs.Count > 100)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }
}
