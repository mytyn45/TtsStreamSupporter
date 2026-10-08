using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Serilog;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public class ConnectionsTabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsService _settings;
    private readonly TtsMessageProcessing _ttsProcessor;
    private readonly MessageQueue _messageQueue;

    private readonly TwitchConnection _twitchConnection;
    private readonly GoodgameConnection _goodgameConnection;
    private readonly BluestarConnection _bluestarConnection;

    private bool _isBusy;

    public ObservableCollection<ConnectionStatus> Connections { get; } = new();

    public ConnectionsTabViewModel(
        SettingsService settings,
        TtsMessageProcessing ttsProcessor,
        MessageQueue messageQueue,
        TwitchConnection twitchConnection,
        GoodgameConnection goodgameConnection,
        BluestarConnection bluestarConnection)
    {
        _settings = settings;
        _ttsProcessor = ttsProcessor;
        _messageQueue = messageQueue;

        _twitchConnection = twitchConnection;
        _goodgameConnection = goodgameConnection;
        _bluestarConnection = bluestarConnection;

        // Сообщения
        _twitchConnection.MessageReceived += OnChatMessageReceived;
        _goodgameConnection.MessageReceived += OnChatMessageReceived;
        _bluestarConnection.MessageReceived += OnChatMessageReceived;

        // Состояния подключений
        SubscribeConnection("Twitch", _twitchConnection);
        SubscribeConnection("GoodGame", _goodgameConnection);
        SubscribeConnection("Bluestar", _bluestarConnection);

        ButtonConnectToChats = new RelayCommand(Connect);
        ButtonReconnectToChats = new RelayCommand(Reconnect);

        // Settings
        _twitchNick = _settings.Settings.TwitchNick;
        _checkboxConnectToTwitch = _settings.Settings.CheckboxConnectToTwitch;

        _goodgameNick = _settings.Settings.GoodgameNick;
        _checkboxConnectToGoodgame = _settings.Settings.CheckboxConnectToGoodgame;

        _bluestarApi = _settings.Settings.BluestarApi;
        _checkboxConnectToBluestar = _settings.Settings.CheckboxConnectToBluestar;

        _ttsDelayEnabled = _settings.Settings.TtsDelayEnabled;
        _ttsDelayTime = _settings.Settings.TtsDelayTime;
    }

    private string? _twitchNick = "";
    private bool _checkboxConnectToTwitch;

    private string? _goodgameNick = "";
    private bool _checkboxConnectToGoodgame;

    private string? _bluestarApi = "";
    private bool _checkboxConnectToBluestar;

    private bool _ttsDelayEnabled;
    private int _ttsDelayTime;

    // =========================================================
    // CONNECTIONS
    // =========================================================

    private void SubscribeConnection(string name, ConnectionBase connection)
    {
        var status = new ConnectionStatus(name, connection.State);
        Connections.Add(status);

        connection.StateChanged += state => OnConnectionStateChanged(status, state);
    }

    private void OnConnectionStateChanged(ConnectionStatus status, ConnectionState state)
    {
        // События приходят из сетевых потоков, UI обновляем в UI-потоке
        if (Dispatcher.UIThread.CheckAccess())
            status.UpdateState(state);
        else
            Dispatcher.UIThread.Post(() => status.UpdateState(state));
    }

    // =========================================================
    // TWITCH
    // =========================================================

    public string? TwitchNick
    {
        get => _twitchNick;
        set
        {
            if (_twitchNick == value)
                return;

            _twitchNick = value;
            OnPropertyChanged();

            _settings.Settings.TwitchNick = value;
            _ = _settings.DelayedSave();
        }
    }

    public bool CheckboxConnectToTwitch
    {
        get => _checkboxConnectToTwitch;
        set
        {
            if (_checkboxConnectToTwitch == value)
                return;

            _checkboxConnectToTwitch = value;
            OnPropertyChanged();

            _settings.Settings.CheckboxConnectToTwitch = value;
            _ = _settings.DelayedSave();
        }
    }

    // =========================================================
    // GOODGAME
    // =========================================================

    public string? GoodgameNick
    {
        get => _goodgameNick;
        set
        {
            if (_goodgameNick == value)
                return;

            _goodgameNick = value;
            OnPropertyChanged();

            _settings.Settings.GoodgameNick = value;
            _ = _settings.DelayedSave();
        }
    }

    public bool CheckboxConnectToGoodgame
    {
        get => _checkboxConnectToGoodgame;
        set
        {
            if (_checkboxConnectToGoodgame == value)
                return;

            _checkboxConnectToGoodgame = value;
            OnPropertyChanged();

            _settings.Settings.CheckboxConnectToGoodgame = value;
            _ = _settings.DelayedSave();
        }
    }

    // =========================================================
    // BLUESTAR
    // =========================================================

    public string? BluestarApi
    {
        get => _bluestarApi;
        set
        {
            if (_bluestarApi == value)
                return;

            _bluestarApi = value;
            OnPropertyChanged();

            _settings.Settings.BluestarApi = value;
            _ = _settings.DelayedSave();
        }
    }

    public bool CheckboxConnectToBluestar
    {
        get => _checkboxConnectToBluestar;
        set
        {
            if (_checkboxConnectToBluestar == value)
                return;

            _checkboxConnectToBluestar = value;
            OnPropertyChanged();

            _settings.Settings.CheckboxConnectToBluestar = value;
            _ = _settings.DelayedSave();
        }
    }

    // =========================================================
    // TTS DELAY
    // =========================================================

    public bool TtsDelayEnabled
    {
        get => _ttsDelayEnabled;
        set
        {
            if (_ttsDelayEnabled == value)
                return;

            _ttsDelayEnabled = value;
            OnPropertyChanged();

            _settings.Settings.TtsDelayEnabled = value;
            _ = _settings.DelayedSave();
        }
    }

    public int TtsDelayTime
    {
        get => _ttsDelayTime;
        set
        {
            if (_ttsDelayTime == value)
                return;

            _ttsDelayTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TtsDelayLabelDisplay));

            _settings.Settings.TtsDelayTime = value;
            _ = _settings.DelayedSave();
        }
    }

    // Вычисляемая метка, например "5с"
    public string TtsDelayLabelDisplay => $"{TtsDelayTime}с";

    // =========================================================
    // CONNECT
    // =========================================================

    public ICommand ButtonConnectToChats { get; }
    public ICommand ButtonReconnectToChats { get; }

    private async void Connect()
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

    private async void Reconnect()
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

    // =========================================================
    // CHAT MESSAGES
    // =========================================================

    private async void OnChatMessageReceived(ChatMessageReceived message)
    {
        try
        {
            var result = await _ttsProcessor.ProcessIncomingMessage(
                message.Username,
                message.VoiceName,
                message.Message,
                message.Platform);

            switch (result.Status)
            {
                case TtsProcessStatus.Success:
                    _messageQueue.Enqueue(result.Message!);
                    break;

                case TtsProcessStatus.Skipped:
                case TtsProcessStatus.Blacklisted:
                case TtsProcessStatus.Filtered:
                    // штатные сценарии, ошибкой логировать не нужно
                    break;

                case TtsProcessStatus.Empty:
                    Log.Debug("Сообщение пришло пустым");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Ошибка при обработке сообщения от {User} ({Platform})",
                message.Username,
                message.Platform);
        }
    }

    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        _twitchConnection.MessageReceived -= OnChatMessageReceived;
        _goodgameConnection.MessageReceived -= OnChatMessageReceived;
        _bluestarConnection.MessageReceived -= OnChatMessageReceived;
    }

    // =========================================================
    // PROPERTY CHANGED
    // =========================================================

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}