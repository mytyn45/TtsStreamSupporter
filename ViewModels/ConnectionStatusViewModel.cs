using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public partial class ConnectionStatusViewModel : ViewModelBase
{
    private static readonly IBrush ConnectedBrush = Brush.Parse("#43A047");
    private static readonly IBrush ConnectingBrush = Brush.Parse("#FB8C00");
    private static readonly IBrush DisconnectedBrush = Brush.Parse("#E53935");
    private static readonly IBrush FailedBrush = Brush.Parse("#D32F2F");
    private static readonly IBrush UnknownBrush = Brush.Parse("#757575");

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    private ConnectionState _state;

    public ConnectionStatusViewModel(string name, ConnectionState state = ConnectionState.Disconnected)
    {
        Name = name;
        _state = state;
    }

    public string StatusText => State switch
    {
        ConnectionState.Disconnected => "Отключён",
        ConnectionState.Connecting => "Подключение...",
        ConnectionState.Connected => "Подключён",
        ConnectionState.Failed => "Ошибка",
        _ => "Неизвестно"
    };

    public IBrush BackgroundBrush => State switch
    {
        ConnectionState.Connected => ConnectedBrush,
        ConnectionState.Connecting => ConnectingBrush,
        ConnectionState.Disconnected => DisconnectedBrush,
        ConnectionState.Failed => FailedBrush,
        _ => UnknownBrush
    };

    public IBrush ForegroundBrush => Brushes.White;

    public void UpdateState(ConnectionState state)
    {
        State = state;
    }
}
