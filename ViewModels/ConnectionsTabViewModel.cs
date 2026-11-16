using System;
using System.Collections.ObjectModel;
using System.Threading;
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
    private readonly NttsHealthCheckService _nttsHealthCheck;

    public NttsHealthCheckService NttsHealth => _nttsHealthCheck;

    public ConnectionStatusViewModel TwitchStatus { get; }
    public ConnectionStatusViewModel GoodgameStatus { get; }
    public ConnectionStatusViewModel KickStatus { get; }
    public ConnectionStatusViewModel BluestarStatus { get; }

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
    private bool _autoReconnectEnabled;

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
        BluestarConnection bluestarConnection,
        NttsHealthCheckService nttsHealthCheck)
    {
        _settings = settings;
        _twitchConnection = twitchConnection;
        _goodgameConnection = goodgameConnection;
        _kickConnection = kickConnection;
        _bluestarConnection = bluestarConnection;
        _nttsHealthCheck = nttsHealthCheck;

        // Инициализация статусов подключений
        TwitchStatus = CreateAndSubscribeConnection("Twitch", _twitchConnection);
        GoodgameStatus = CreateAndSubscribeConnection("GoodGame", _goodgameConnection);
        KickStatus = CreateAndSubscribeConnection("Kick", _kickConnection);
        BluestarStatus = CreateAndSubscribeConnection("Bluestar", _bluestarConnection);

        // Загрузка настроек
        _twitchNick = _settings.Settings.TwitchNick;
        _checkboxConnectToTwitch = _settings.Settings.CheckboxConnectToTwitch;

        _goodgameNick = _settings.Settings.GoodgameNick;
        _checkboxConnectToGoodgame = _settings.Settings.CheckboxConnectToGoodgame;

        _kickNick = _settings.Settings.KickNick;
        _checkboxConnectToKick = _settings.Settings.CheckboxConnectToKick;

        _bluestarApi = _settings.Settings.BluestarApi;
        _checkboxConnectToBluestar = _settings.Settings.CheckboxConnectToBluestar;

        _autoReconnectEnabled = _settings.Settings.AutoReconnectEnabled;
        if (_autoReconnectEnabled)
        {
            StartAutoReconnectTimer(initialDelayMs: 3000);
        }

        _ttsDelayEnabled = _settings.Settings.TtsDelayEnabled;
        _ttsDelayTime = _settings.Settings.TtsDelayTime;

        // Первичный опрос серверов NTTS для отображения актуального статуса
        _ = _nttsHealthCheck.PerformHealthCheckAndUpdateStatusAsync();
    }

    private ConnectionStatus CreateAndSubscribeConnection(string name, ConnectionBase connection)
    {
        var status = new ConnectionStatus(name, connection.State);
        Connections.Add(status);

        connection.StateChanged += state => OnConnectionStateChanged(status, state);
        return status;
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

    partial void OnAutoReconnectEnabledChanged(bool value)
    {
        _settings.Settings.AutoReconnectEnabled = value;
        _ = _settings.DelayedSave();

        if (value)
        {
            StartAutoReconnectTimer(initialDelayMs: 500);
        }
        else
        {
            StopAutoReconnectTimer();
        }
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

        await _reconnectLock.WaitAsync();
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
            _reconnectLock.Release();
        }
    }

    [RelayCommand]
    public async Task ReconnectToChatsAsync()
    {
        if (_isBusy)
            return;

        await _reconnectLock.WaitAsync();
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
            _reconnectLock.Release();
        }
    }

    // Алиасы для привязок XAML: ButtonConnectToChats, ButtonReconnectToChats
    public IAsyncRelayCommand ButtonConnectToChats => ConnectToChatsCommand;
    public IAsyncRelayCommand ButtonReconnectToChats => ReconnectToChatsCommand;

    private CancellationTokenSource? _autoReconnectCts;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    private void StartAutoReconnectTimer(int initialDelayMs = 0)
    {
        StopAutoReconnectTimer();
        _autoReconnectCts = new CancellationTokenSource();
        var token = _autoReconnectCts.Token;
        Task.Run(() => AutoReconnectLoopAsync(initialDelayMs, token), token);
    }

    private void StopAutoReconnectTimer()
    {
        try
        {
            _autoReconnectCts?.Cancel();
            _autoReconnectCts?.Dispose();
        }
        catch { }
        _autoReconnectCts = null;
    }

    private async Task AutoReconnectLoopAsync(int initialDelayMs, CancellationToken ct)
    {
        if (initialDelayMs > 0)
        {
            try
            {
                await Task.Delay(initialDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAndReconnectServicesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка в фоновом цикле авто-переподключения");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAndReconnectServicesAsync(CancellationToken ct)
    {
        if (_isBusy || !AutoReconnectEnabled)
            return;

        if (!await _reconnectLock.WaitAsync(0, ct))
            return;

        _isBusy = true;
        try
        {
            // 1. Twitch
            if (CheckboxConnectToTwitch && !string.IsNullOrWhiteSpace(TwitchNick))
            {
                bool isHealthy = false;
                try
                {
                    isHealthy = await _twitchConnection.CheckHealthAsync(ct);
                }
                catch { }

                if (!isHealthy && !ct.IsCancellationRequested && CheckboxConnectToTwitch)
                {
                    Log.Warning("Авто-переподключение: потеряна связь с Twitch #{Nick} (или нет ответа). Переподключение...", TwitchNick);
                    try
                    {
                        await _twitchConnection.ReconnectAsync(TwitchNick);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Ошибка при авто-переподключении к Twitch");
                    }
                }
            }

            // 2. Goodgame
            if (CheckboxConnectToGoodgame && !string.IsNullOrWhiteSpace(GoodgameNick))
            {
                bool isHealthy = false;
                try
                {
                    isHealthy = await _goodgameConnection.CheckHealthAsync(ct);
                }
                catch { }

                if (!isHealthy && !ct.IsCancellationRequested && CheckboxConnectToGoodgame)
                {
                    Log.Warning("Авто-переподключение: потеряна связь с GoodGame {Nick} (или нет ответа). Переподключение...", GoodgameNick);
                    try
                    {
                        await _goodgameConnection.ReconnectAsync(GoodgameNick);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Ошибка при авто-переподключении к GoodGame");
                    }
                }
            }

            // 3. Kick
            if (CheckboxConnectToKick && !string.IsNullOrWhiteSpace(KickNick))
            {
                bool isHealthy = false;
                try
                {
                    isHealthy = await _kickConnection.CheckHealthAsync(ct);
                }
                catch { }

                if (!isHealthy && !ct.IsCancellationRequested && CheckboxConnectToKick)
                {
                    Log.Warning("Авто-переподключение: потеряна связь с Kick {Nick} (или нет ответа). Переподключение...", KickNick);
                    try
                    {
                        await _kickConnection.ReconnectAsync(KickNick);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Ошибка при авто-переподключении к Kick");
                    }
                }
            }

            // 4. Bluestar
            if (CheckboxConnectToBluestar && !string.IsNullOrWhiteSpace(BluestarApi))
            {
                bool isHealthy = false;
                try
                {
                    isHealthy = await _bluestarConnection.CheckHealthAsync(ct);
                }
                catch { }

                if (!isHealthy && !ct.IsCancellationRequested && CheckboxConnectToBluestar)
                {
                    Log.Warning("Авто-переподключение: потеряна связь с Bluestar (или нет ответа). Переподключение...");
                    try
                    {
                        await _bluestarConnection.ReconnectAsync(BluestarApi);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Ошибка при авто-переподключении к Bluestar");
                    }
                }
            }
        }
        finally
        {
            _isBusy = false;
            _reconnectLock.Release();
        }
    }

    public void Dispose()
    {
        StopAutoReconnectTimer();
        _reconnectLock.Dispose();
    }
}
