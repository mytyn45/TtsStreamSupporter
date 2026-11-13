using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public partial class NttsTabViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly SpeechPlayerService? _speechPlayerService;
    private readonly IndividualVoiceService? _individualVoiceService;
    private readonly NttsHealthCheckService? _nttsHealthCheckService;
    private bool _isProgrammaticNttsChange;

    [ObservableProperty]
    private bool _checkBoxNttsVoicesIsEnabled;

    [ObservableProperty]
    private bool _nttsAutoDisableOnFailure = true;

    [ObservableProperty]
    private bool _nttsPeriodicPingEnabled = true;

    [ObservableProperty]
    private string? _nttsApi = "";

    [ObservableProperty]
    private string _nttsVoiceChangeCommand = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentServerStatus))]
    private bool _nttsUseFallbackServer;

    public string CurrentServerStatus => NttsUseFallbackServer
        ? "Активный сервер: ntts.fdev.team (запасной)"
        : "Активный сервер: nttsfuckrkn.fdev.team (основной)";

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<NttsVoiceCatalogEntry> Voices { get; } = new();

    public NttsTabViewModel(
        SettingsService settings,
        SpeechPlayerService? speechPlayerService = null,
        IndividualVoiceService? individualVoiceService = null,
        NttsHealthCheckService? nttsHealthCheckService = null)
    {
        _settings = settings;
        _speechPlayerService = speechPlayerService;
        _individualVoiceService = individualVoiceService;
        _nttsHealthCheckService = nttsHealthCheckService;

        _nttsApi = _settings.Settings.NttsApi;
        _nttsVoiceChangeCommand = _settings.Settings.NttsVoiceChangeCommand;
        _checkBoxNttsVoicesIsEnabled = _settings.Settings.CheckBoxNttsVoicesIsEnabled;
        _nttsUseFallbackServer = _settings.Settings.NttsUseFallbackServer;
        _nttsAutoDisableOnFailure = _settings.Settings.NttsAutoDisableOnFailure;
        _nttsPeriodicPingEnabled = _settings.Settings.NttsPeriodicPingEnabled;

        if (_speechPlayerService != null)
        {
            _speechPlayerService.ActiveServerChanged += OnActiveServerChanged;
        }

        if (_nttsHealthCheckService != null)
        {
            _nttsHealthCheckService.RequestSetNttsEnabled += enabled =>
            {
                _isProgrammaticNttsChange = true;
                try
                {
                    CheckBoxNttsVoicesIsEnabled = enabled;
                }
                finally
                {
                    _isProgrammaticNttsChange = false;
                }
            };

            _nttsHealthCheckService.RequestSetFallbackServer += isFallback =>
            {
                NttsUseFallbackServer = isFallback;
            };
        }

        LoadLocalVoicesIfAvailable();
    }

    private void OnActiveServerChanged(bool isFallback)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (NttsUseFallbackServer != isFallback)
            {
                NttsUseFallbackServer = isFallback;
            }
        });
    }

    partial void OnCheckBoxNttsVoicesIsEnabledChanged(bool value)
    {
        _settings.Settings.CheckBoxNttsVoicesIsEnabled = value;
        _ = _settings.DelayedSave();

        if (!_isProgrammaticNttsChange)
        {
            _nttsHealthCheckService?.CancelTemporaryDisable();
        }
    }

    partial void OnNttsAutoDisableOnFailureChanged(bool value)
    {
        _settings.Settings.NttsAutoDisableOnFailure = value;
        _ = _settings.DelayedSave();

        if (!value)
        {
            _nttsHealthCheckService?.CancelTemporaryDisable();
        }
    }

    partial void OnNttsPeriodicPingEnabledChanged(bool value)
    {
        _settings.Settings.NttsPeriodicPingEnabled = value;
        _ = _settings.DelayedSave();
        _nttsHealthCheckService?.SetPeriodicPingEnabled(value);
    }

    partial void OnNttsApiChanged(string? value)
    {
        _settings.Settings.NttsApi = value;
        _ = _settings.DelayedSave();
    }

    partial void OnNttsVoiceChangeCommandChanged(string value)
    {
        _settings.Settings.NttsVoiceChangeCommand = value;
        _ = _settings.DelayedSave();
    }

    partial void OnNttsUseFallbackServerChanged(bool value)
    {
        _settings.Settings.NttsUseFallbackServer = value;
        _ = _settings.DelayedSave();

        if (_speechPlayerService != null)
        {
            _speechPlayerService.UseFallbackServer = value;
        }
    }

    private void LoadLocalVoicesIfAvailable()
    {
        try
        {
            string? path = null;
            if (File.Exists(DirectoryService.NttsVoicesFilePath))
                path = DirectoryService.NttsVoicesFilePath;
            else if (File.Exists(DirectoryService.NttsVoicesDataForProgramFilePath))
                path = DirectoryService.NttsVoicesDataForProgramFilePath;

            if (path == null)
                return;

            string json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<NttsVoicesFile>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result != null)
            {
                Voices.Clear();
                foreach (var voice in result.Voices)
                    Voices.Add(voice);
                foreach (var voice in result.CustomVoices)
                    Voices.Add(voice);

                StatusMessage = $"Загружено локальных голосов: {Voices.Count}";
            }
        }
        catch
        {
        }
    }

    [RelayCommand]
    public async Task LoadVoicesAsync()
    {
        try
        {
            StatusMessage = "Загрузка...";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            string serverUrl = NttsUseFallbackServer
                ? "https://ntts.fdev.team/api/v1/tts/speakers"
                : "https://nttsfuckrkn.fdev.team/api/v1/tts/speakers";

            var response = await client.GetAsync(serverUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            try
            {
                string? dir = Path.GetDirectoryName(DirectoryService.NttsVoicesFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(DirectoryService.NttsVoicesFilePath, json);

                string? dfpDir = Path.GetDirectoryName(DirectoryService.NttsVoicesDataForProgramFilePath);
                if (!string.IsNullOrEmpty(dfpDir) && !Directory.Exists(dfpDir))
                    Directory.CreateDirectory(dfpDir);

                File.WriteAllText(DirectoryService.NttsVoicesDataForProgramFilePath, json);

                _individualVoiceService?.ReloadNttsVoices();
            }
            catch
            {
            }

            var result = JsonSerializer.Deserialize<NttsVoicesFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            Voices.Clear();

            if (result is null)
            {
                StatusMessage = "Пустой ответ от сервера";
                return;
            }

            foreach (var voice in result.Voices)
                Voices.Add(voice);

            foreach (var voice in result.CustomVoices)
                Voices.Add(voice);

            StatusMessage = $"Загружено голосов: {Voices.Count}";
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "Таймаут";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Ошибка сети: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка открытия ссылки: {ex.Message}";
        }
    }

    // Алиасы для привязок XAML
    public IAsyncRelayCommand ButtonLoadVoices => LoadVoicesCommand;
}
