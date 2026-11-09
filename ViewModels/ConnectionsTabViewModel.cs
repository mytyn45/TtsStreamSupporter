using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public partial class ConnectionsTabViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settings;
    private readonly TwitchConnection _twitchConnection;
    private readonly GoodgameConnection _goodgameConnection;
    private readonly KickConnection _kickConnection;
    private readonly BluestarConnection _bluestarConnection;

    private bool _isBusy;

    public ObservableCollection<ConnectionStatus> Connections { get; } = new();

    [ObservableProperty]
    private string? _twitchNick = "";

    [ObservableProperty]
    private bool _checkboxConnectToTwitch;

    [ObservableProperty]
    private string? _goodgameNick = "";

    [ObservableProperty]
    private bool _checkboxConnectToGoodgame;

    [ObservableProperty]
    private string? _kickNick = "";

    [ObservableProperty]
    private bool _checkboxConnectToKick;

    [ObservableProperty]
    private string? _bluestarApi = "";

    [ObservableProperty]
    private bool _checkboxConnectToBluestar;

    [ObservableProperty]
    private bool _ttsDelayEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TtsDelayLabelDisplay))]
    private int _ttsDelayTime;

    public string TtsDelayLabelDisplay => $"{TtsDelayTime}с";

    public ConnectionsTabViewModel(
        SettingsService settings,
        TwitchConnection twitchConnection,
        GoodgameConnection goodgameConnection,
        KickConnection kickConnection,
        BluestarConnection bluestarConnection)
    {
        _settings = settings;
        _twitchConnection = twitchConnection;
        _goodgameConnection = goodgameConnection;
        _kickConnection = kickConnection;
        _bluestarConnection = bluestarConnection;

        // Инициализация статусов подключений
        SubscribeConnection("Twitch", _twitchConnection);
        SubscribeConnection("GoodGame", _goodgameConnection);
        SubscribeConnection("Kick", _kickConnection);
        SubscribeConnection("Bluestar", _bluestarConnection);

        // Загрузка настроек
        _twitchNick = _settings.Settings.TwitchNick;
        _checkboxConnectToTwitch = _settings.Settings.CheckboxConnectToTwitch;

        _goodgameNick = _settings.Settings.GoodgameNick;
        _checkboxConnectToGoodgame = _settings.Settings.CheckboxConnectToGoodgame;

        _kickNick = _settings.Settings.KickNick;
        _checkboxConnectToKick = _settings.Settings.CheckboxConnectToKick;

        _bluestarApi = _settings.Settings.BluestarApi;
        _checkboxConnectToBluestar = _settings.Settings.CheckboxConnectToBluestar;

        _ttsDelayEnabled = _settings.Settings.TtsDelayEnabled;
        _ttsDelayTime = _settings.Settings.TtsDelayTime;
    }

    private void SubscribeConnection(string name, ConnectionBase connection)
    {
        var status = new ConnectionStatus(name, connection.State);
        Connections.Add(status);

        connection.StateChanged += state => OnConnectionStateChanged(status, state);
    }

    private static void OnConnectionStateChanged(ConnectionStatus status, ConnectionState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            status.UpdateState(state);
        else
            Dispatcher.UIThread.Post(() => status.UpdateState(state));
    }

    partial void OnTwitchNickChanged(string? value)
    {
        _settings.Settings.TwitchNick = value;
        _ = _settings.DelayedSave();
    }

    partial void OnCheckboxConnectToTwitchChanged(bool value)
    {
        _settings.Settings.CheckboxConnectToTwitch = value;
        _ = _settings.DelayedSave();
    }

    partial void OnGoodgameNickChanged(string? value)
    {
        _settings.Settings.GoodgameNick = value ?? "";
        _ = _settings.DelayedSave();
    }

    partial void OnCheckboxConnectToGoodgameChanged(bool value)
    {
        _settings.Settings.CheckboxConnectToGoodgame = value;
        _ = _settings.DelayedSave();
    }

    partial void OnKickNickChanged(string? value)
    {
        _settings.Settings.KickNick = value;
        _ = _settings.DelayedSave();
    }

    partial void OnCheckboxConnectToKickChanged(bool value)
    {
        _settings.Settings.CheckboxConnectToKick = value;
        _ = _settings.DelayedSave();
    }

    partial void OnBluestarApiChanged(string? value)
    {
        _settings.Settings.BluestarApi = value;
        _ = _settings.DelayedSave();
    }

    partial void OnCheckboxConnectToBluestarChanged(bool value)
    {
        _settings.Settings.CheckboxConnectToBluestar = value;
        _ = _settings.DelayedSave();
    }

    partial void OnTtsDelayEnabledChanged(bool value)
    {
        _settings.Settings.TtsDelayEnabled = value;
        _ = _settings.DelayedSave();
    }

    partial void OnTtsDelayTimeChanged(int value)
    {
        _settings.Settings.TtsDelayTime = value;
        _ = _settings.DelayedSave();
    }

    [RelayCommand]
    public async Task ConnectToChatsAsync()
    {
        if (_isBusy)
            return;

        _isBusy = true;
        try
        {
            if (CheckboxConnectToTwitch)
            {
                Log.Information("Подключение к Twitch...");
                await _twitchConnection.ConnectAsync(TwitchNick);
            }

            if (CheckboxConnectToGoodgame)
            {
                Log.Information("Подключение к GoodGame...");
                await _goodgameConnection.ConnectAsync(GoodgameNick);
            }

            if (CheckboxConnectToKick)
            {
                Log.Information("Подключение к Kick...");
                await _kickConnection.ConnectAsync(KickNick);
            }

            if (CheckboxConnectToBluestar)
            {
                Log.Information("Подключение к Bluestar...");
                await _bluestarConnection.ConnectAsync(BluestarApi);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при подключении к чатам");
        }
        finally
        {
            _isBusy = false;
        }
    }

    [RelayCommand]
    public async Task ReconnectToChatsAsync()
    {
        if (_isBusy)
            return;

        _isBusy = true;
        try
        {
            Log.Information("Переподключение к чатам...");

            if (CheckboxConnectToTwitch)
            {
                Log.Information("Переподключение к Twitch...");
                await _twitchConnection.ReconnectAsync(TwitchNick);
            }
            else
            {
                _twitchConnection.Disconnect();
            }

            if (CheckboxConnectToGoodgame)
            {
                Log.Information("Переподключение к GoodGame...");
                await _goodgameConnection.ReconnectAsync(GoodgameNick);
            }
            else
            {
                _goodgameConnection.Disconnect();
            }

            if (CheckboxConnectToKick)
            {
                Log.Information("Переподключение к Kick...");
                await _kickConnection.ReconnectAsync(KickNick);
            }
            else
            {
                _kickConnection.Disconnect();
            }

            if (CheckboxConnectToBluestar)
            {
                Log.Information("Переподключение к Bluestar...");
                await _bluestarConnection.ReconnectAsync(BluestarApi);
            }
            else
            {
                _bluestarConnection.Disconnect();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при переподключении к чатам");
        }
        finally
        {
            _isBusy = false;
        }
    }

    // Алиасы для привязок XAML: ButtonConnectToChats, ButtonReconnectToChats
    public IAsyncRelayCommand ButtonConnectToChats => ConnectToChatsCommand;
    public IAsyncRelayCommand ButtonReconnectToChats => ReconnectToChatsCommand;

    public void Dispose()
    {
    }
}
