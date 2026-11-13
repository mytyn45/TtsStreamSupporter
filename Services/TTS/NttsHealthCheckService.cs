using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace TtsStreamSupporter.Services;

/// <summary>
/// Сервис мониторинга доступности серверов NTTS.
/// Поддерживает:
/// 1. Регулярный фоновый опрос раз в минуту (если включена опция в настройках).
/// 2. Аварийный режим при сбое обоих серверов (отключение NTTS, опрос каждые 10 сек с авто-включением).
/// 3. Постоянное отображение статуса серверов в нижней части окна.
/// </summary>
public partial class NttsHealthCheckService : ObservableObject, IDisposable
{
    private static readonly IBrush GreenBg = Brush.Parse("#E8F5E9");
    private static readonly IBrush GreenBorder = Brush.Parse("#4CAF50");
    private static readonly IBrush GreenFg = Brush.Parse("#2E7D32");

    private static readonly IBrush OrangeBg = Brush.Parse("#FFF3E0");
    private static readonly IBrush OrangeBorder = Brush.Parse("#FF9800");
    private static readonly IBrush OrangeFg = Brush.Parse("#E65100");

    private static readonly IBrush RedBg = Brush.Parse("#FFEBEE");
    private static readonly IBrush RedBorder = Brush.Parse("#EF5350");
    private static readonly IBrush RedFg = Brush.Parse("#C62828");

    private static readonly IBrush GrayBg = Brush.Parse("#F5F5F5");
    private static readonly IBrush GrayBorder = Brush.Parse("#BDBDBD");
    private static readonly IBrush GrayFg = Brush.Parse("#616161");

    private readonly SettingsService _settings;
    private readonly object _lock = new();

    private CancellationTokenSource? _periodicCts;
    private CancellationTokenSource? _emergencyCts;

    private static readonly HttpClient PrimaryHttpClient = new()
    {
        BaseAddress = new Uri("https://nttsfuckrkn.fdev.team"),
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly HttpClient FallbackHttpClient = new()
    {
        BaseAddress = new Uri("https://ntts.fdev.team"),
        Timeout = TimeSpan.FromSeconds(4)
    };

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusText = "NTTS: проверка связи...";

    [ObservableProperty]
    private string _statusIcon = "🔄";

    [ObservableProperty]
    private IBrush _statusBackgroundBrush = GrayBg;

    [ObservableProperty]
    private IBrush _statusBorderBrush = GrayBorder;

    [ObservableProperty]
    private IBrush _statusForegroundBrush = GrayFg;

    [ObservableProperty]
    private bool _isEmergencyActive;

    [ObservableProperty]
    private int _secondsUntilNextEmergencyPing = 10;

    /// <summary>
    /// Запрос на изменение состояния чекбокса NTTS (вызывается из сервиса на UI-потоке)
    /// </summary>
    public event Action<bool>? RequestSetNttsEnabled;

    /// <summary>
    /// Запрос на переключение активного сервера (основной/запасной)
    /// </summary>
    public event Action<bool>? RequestSetFallbackServer;

    public NttsHealthCheckService(SettingsService settings)
    {
        _settings = settings;

        // Если включен периодический пинг в настройках, запускаем его сразу
        if (_settings.Settings.NttsPeriodicPingEnabled)
        {
            StartPeriodicPingLoop();
        }
    }

    /// <summary>
    /// Включает или выключает периодический пинг серверов (раз в минуту).
    /// </summary>
    public void SetPeriodicPingEnabled(bool enabled)
    {
        lock (_lock)
        {
            if (enabled)
            {
                StartPeriodicPingLoop();
            }
            else
            {
                StopPeriodicPingLoop();
            }
        }
    }

    private void StartPeriodicPingLoop()
    {
        lock (_lock)
        {
            _periodicCts?.Cancel();
            _periodicCts?.Dispose();
            _periodicCts = new CancellationTokenSource();

            IsStatusVisible = true;
            _ = RunPeriodicPingLoopAsync(_periodicCts.Token);
        }
    }

    private void StopPeriodicPingLoop()
    {
        lock (_lock)
        {
            _periodicCts?.Cancel();
            _periodicCts?.Dispose();
            _periodicCts = null;

            if (!IsEmergencyActive)
            {
                IsStatusVisible = false;
            }
        }
    }

    private async Task RunPeriodicPingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Не запускаем обычный опрос, если сейчас активен аварийный режим
                if (!IsEmergencyActive)
                {
                    await PerformHealthCheckAndUpdateStatusAsync(ct);
                }

                // Ждём 60 секунд до следующего опроса
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная отмена
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка в фоновом периодическом пинге NTTS");
        }
    }

    /// <summary>
    /// Выполняет один опрос серверов и обновляет плашку статуса
    /// </summary>
    public async Task PerformHealthCheckAndUpdateStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var (primaryOk, fallbackOk) = await CheckBothServersAsync(ct);

            Dispatcher.UIThread.Post(() =>
            {
                if (IsEmergencyActive)
                    return; // В аварийном режиме статус управляется его циклом

                UpdateStatusDisplay(primaryOk, fallbackOk);
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Не удалось выполнить проверку серверов NTTS");
        }
    }

    private void UpdateStatusDisplay(bool primaryOk, bool fallbackOk)
    {
        IsStatusVisible = _settings.Settings.NttsPeriodicPingEnabled || IsEmergencyActive;

        if (primaryOk && fallbackOk)
        {
            StatusIcon = "✔";
            StatusText = "NTTS: основной ✔, запасной ✔";
            StatusBackgroundBrush = GreenBg;
            StatusBorderBrush = GreenBorder;
            StatusForegroundBrush = GreenFg;
        }
        else if (primaryOk && !fallbackOk)
        {
            StatusIcon = "⚠";
            StatusText = "NTTS: основной ✔, запасной ❌";
            StatusBackgroundBrush = OrangeBg;
            StatusBorderBrush = OrangeBorder;
            StatusForegroundBrush = OrangeFg;
        }
        else if (!primaryOk && fallbackOk)
        {
            StatusIcon = "⚠";
            StatusText = "NTTS: основной ❌, запасной ✔";
            StatusBackgroundBrush = OrangeBg;
            StatusBorderBrush = OrangeBorder;
            StatusForegroundBrush = OrangeFg;
        }
        else
        {
            StatusIcon = "❌";
            StatusText = "NTTS: оба сервера недоступны ❌";
            StatusBackgroundBrush = RedBg;
            StatusBorderBrush = RedBorder;
            StatusForegroundBrush = RedFg;
        }
    }

    /// <summary>
    /// Вызывается при сбое обоих NTTS серверов во время генерации речи.
    /// Переключает систему в аварийный режим с учащенным опросом (раз в 10 сек).
    /// </summary>
    public void NotifyBothServersFailed()
    {
        if (!_settings.Settings.NttsAutoDisableOnFailure)
        {
            Log.Debug("NTTS: авто-отключение при сбое выключено в настройках.");
            return;
        }

        lock (_lock)
        {
            if (IsEmergencyActive)
                return;

            IsEmergencyActive = true;
            IsStatusVisible = true;
            SecondsUntilNextEmergencyPing = 10;

            StatusIcon = "🔄";
            StatusText = "NTTS: серверы не отвечают! Пинг через 10 с...";
            StatusBackgroundBrush = OrangeBg;
            StatusBorderBrush = OrangeBorder;
            StatusForegroundBrush = OrangeFg;

            _emergencyCts?.Cancel();
            _emergencyCts?.Dispose();
            _emergencyCts = new CancellationTokenSource();
        }

        Log.Warning("NTTS: оба сервера недоступны! Временно отключаем NTTS и запускаем проверку связи каждые 10 сек.");

        Dispatcher.UIThread.Post(() =>
        {
            RequestSetNttsEnabled?.Invoke(false);
        });

        _ = RunEmergencyPingLoopAsync(_emergencyCts.Token);
    }

    /// <summary>
    /// Прерывает режим аварийного отключения (например, если пользователь вручную включил/выключил галочку)
    /// </summary>
    public void CancelTemporaryDisable()
    {
        lock (_lock)
        {
            if (!IsEmergencyActive)
                return;

            _emergencyCts?.Cancel();
            _emergencyCts?.Dispose();
            _emergencyCts = null;
            IsEmergencyActive = false;

            Dispatcher.UIThread.Post(() =>
            {
                if (_settings.Settings.NttsPeriodicPingEnabled)
                {
                    _ = PerformHealthCheckAndUpdateStatusAsync();
                }
                else
                {
                    IsStatusVisible = false;
                }
            });
        }

        Log.Information("NTTS: аварийный пинг серверов отменен.");
    }

    private async Task RunEmergencyPingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 10-секундный обратный отсчет
                for (int sec = 10; sec > 0; sec--)
                {
                    int currentSec = sec;
                    Dispatcher.UIThread.Post(() =>
                    {
                        SecondsUntilNextEmergencyPing = currentSec;
                        StatusIcon = "🔄";
                        StatusText = $"NTTS: серверы не отвечают! Пинг через {currentSec} с...";
                    });

                    await Task.Delay(1000, ct);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "NTTS: проверка связи с серверами...";
                });

                var (primaryOk, fallbackOk) = await CheckBothServersAsync(ct);

                if (primaryOk || fallbackOk)
                {
                    string recoveredHost = primaryOk ? "nttsfuckrkn.fdev.team (основной)" : "ntts.fdev.team (запасной)";
                    Log.Information("NTTS: сервер {Host} снова доступен! Восстанавливаем работу NTTS.", recoveredHost);

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!primaryOk && fallbackOk)
                        {
                            RequestSetFallbackServer?.Invoke(true);
                        }
                        else if (primaryOk)
                        {
                            RequestSetFallbackServer?.Invoke(false);
                        }

                        // Включаем обратно галочку NTTS
                        RequestSetNttsEnabled?.Invoke(true);

                        IsEmergencyActive = false;
                        UpdateStatusDisplay(primaryOk, fallbackOk);
                    });

                    lock (_lock)
                    {
                        _emergencyCts?.Dispose();
                        _emergencyCts = null;
                    }

                    return;
                }

                Log.Debug("NTTS: серверы по-прежнему не отвечают, следующий пинг через 10 сек.");
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная отмена
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка в аварийном цикле проверки NTTS");
            Dispatcher.UIThread.Post(() =>
            {
                IsEmergencyActive = false;
                if (!_settings.Settings.NttsPeriodicPingEnabled)
                    IsStatusVisible = false;
            });
        }
    }

    private static async Task<(bool primaryOk, bool fallbackOk)> CheckBothServersAsync(CancellationToken ct)
    {
        var primaryTask = PingServerAsync(PrimaryHttpClient, ct);
        var fallbackTask = PingServerAsync(FallbackHttpClient, ct);

        await Task.WhenAll(primaryTask, fallbackTask);

        return (primaryTask.Result, fallbackTask.Result);
    }

    private static async Task<bool> PingServerAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tts/speakers");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            int statusCode = (int)response.StatusCode;
            return statusCode is >= 200 and < 500;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _periodicCts?.Cancel();
        _periodicCts?.Dispose();
        _periodicCts = null;

        _emergencyCts?.Cancel();
        _emergencyCts?.Dispose();
        _emergencyCts = null;
    }
}
