using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public class NttsTabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly SettingsService _settings;
    private bool _checkBoxNttsVoicesIsEnabled;
    public bool CheckBoxNttsVoicesIsEnabled
    {
        get => _checkBoxNttsVoicesIsEnabled;
        set
        {
            _checkBoxNttsVoicesIsEnabled = value;
            _settings.Settings.CheckBoxNttsVoicesIsEnabled = value;
            OnPropertyChanged();
            _settings.DelayedSave();
        }
    }
    private string? _nttsApi;
    public string NttsApi
    {
        get => _nttsApi;
        set
        {
            _nttsApi = value;
            _settings.Settings.NttsApi = value;
            OnPropertyChanged();
            _ = _settings.DelayedSave();
        }
    }

    private string _nttsVoiceChangeCommand = "";
    public string NttsVoiceChangeCommand
    {
        get => _nttsVoiceChangeCommand;
        set
        {
            if (_nttsVoiceChangeCommand == value)
                return;

            _nttsVoiceChangeCommand = value;
            _settings.Settings.NttsVoiceChangeCommand = value;
            OnPropertyChanged();
            _ = _settings.DelayedSave();
        }
    }

    private bool _nttsUseFallbackServer;
    public bool NttsUseFallbackServer
    {
        get => _nttsUseFallbackServer;
        set
        {
            if (_nttsUseFallbackServer == value)
                return;

            _nttsUseFallbackServer = value;
            _settings.Settings.NttsUseFallbackServer = value;
            _ = _settings.DelayedSave();

            if (_speechPlayerService != null)
            {
                _speechPlayerService.UseFallbackServer = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentServerStatus));
        }
    }

    public string CurrentServerStatus =>
        _nttsUseFallbackServer
            ? "Активный сервер: ntts.fdev.team (запасной)"
            : "Активный сервер: nttsfuckrkn.fdev.team (основной)";

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Voice> Voices { get; set; } = new ObservableCollection<Voice>();

    public ICommand ButtonLoadVoices { get; }
    public ICommand OpenUrlCommand { get; }

    private readonly IndividualVoiceService? _individualVoiceService;
    private readonly SpeechPlayerService? _speechPlayerService;

    public NttsTabViewModel(
        SettingsService settings,
        SpeechPlayerService? speechPlayerService = null,
        IndividualVoiceService? individualVoiceService = null)
    {
        _settings = settings;
        _speechPlayerService = speechPlayerService;
        _individualVoiceService = individualVoiceService;
        _nttsApi = _settings.Settings.NttsApi;
        _nttsVoiceChangeCommand = _settings.Settings.NttsVoiceChangeCommand;
        _checkBoxNttsVoicesIsEnabled = _settings.Settings.CheckBoxNttsVoicesIsEnabled;
        _nttsUseFallbackServer = _settings.Settings.NttsUseFallbackServer;

        if (_speechPlayerService != null)
        {
            _speechPlayerService.ActiveServerChanged += OnActiveServerChanged;
        }

        ButtonLoadVoices = new RelayCommand(LoadVoices);
        OpenUrlCommand = new RelayCommand<string>(OpenUrl);

        LoadLocalVoicesIfAvailable();
    }

    private void OnActiveServerChanged(bool isFallback)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_nttsUseFallbackServer != isFallback)
            {
                _nttsUseFallbackServer = isFallback;
                OnPropertyChanged(nameof(NttsUseFallbackServer));
                OnPropertyChanged(nameof(CurrentServerStatus));
            }
        });
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
            var result = JsonSerializer.Deserialize<VoicesResponse>(
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
        catch { }
    }

    private async void LoadVoices()
    {
        try
        {
            StatusMessage = "Загрузка...";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            string serverUrl = _nttsUseFallbackServer
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
            catch { }

            var result = JsonSerializer.Deserialize<VoicesResponse>(
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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class VoicesResponse
{
    [JsonPropertyName("voices")]
    public List<Voice> Voices { get; set; } = new();

    [JsonPropertyName("custom_voices")]
    public List<Voice> CustomVoices { get; set; } = new();
}

public class Voice
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("speakers")]
    public List<string> Speakers { get; set; } = new();
}