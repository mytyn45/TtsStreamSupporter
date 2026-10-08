using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace TtsStreamSupporter.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public abstract class ConnectionBase
{
    public ConnectionState State { get; private set; }
        = ConnectionState.Disconnected;

    public event Action<ConnectionState>? StateChanged;

    protected void SetState(ConnectionState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
    }
}
public class ConnectionStatus : INotifyPropertyChanged
{
    private ConnectionState _state;

    public string Name { get; }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(BackgroundBrush));
            OnPropertyChanged(nameof(ForegroundBrush));
        }
    }

    public string StatusText =>
        State switch
        {
            ConnectionState.Disconnected => "Отключён",
            ConnectionState.Connecting => "Подключение...",
            ConnectionState.Connected => "Подключён",
            ConnectionState.Failed => "Ошибка",

            _ => "Неизвестно"
        };

    public IBrush BackgroundBrush =>
        State switch
        {
            ConnectionState.Connected => Brush.Parse("#43A047"),
            ConnectionState.Connecting => Brush.Parse("#FB8C00"),
            ConnectionState.Disconnected => Brush.Parse("#E53935"),
            ConnectionState.Failed => Brush.Parse("#D32F2F"),
            _ => Brush.Parse("#757575")
        };

    public IBrush ForegroundBrush => Brushes.White;

    public ConnectionStatus(
        string name,
        ConnectionState state)
    {
        Name = name;
        _state = state;
    }

    public void UpdateState(ConnectionState state)
    {
        State = state;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}