using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Serilog;
using System.Collections.Generic;
using TtsStreamSupporter.Models;
using System.Dynamic;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public class SettingsService
{
    public static string AppDataFolder => DirectoryService.AppDataFolder;

    private readonly string _settingsFile = DirectoryService.SettingsFilePath;
    private readonly string _legacySettingsFile =
        Path.Combine(AppContext.BaseDirectory, "DataForProgram", "Settings.json");

    private CancellationTokenSource? _saveCts;

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        Settings = new AppSettings();
    }

    public async Task DelayedSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(1000, _saveCts.Token);
            Save();
        }
        catch
        {

        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(Settings, options);

            File.WriteAllText(_settingsFile, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка сохранения настроек в {File}", _settingsFile);
        }
    }

    public void Load()
    {
        string fileToLoad = _settingsFile;

        if (!File.Exists(_settingsFile))
        {
            if (File.Exists(_legacySettingsFile))
            {
                fileToLoad = _legacySettingsFile;
                Log.Information("Настройки не найдены в AppData, загружаются из старого пути: {LegacyFile}", _legacySettingsFile);
            }
            else
            {
                string cwdLegacy = Path.Combine(Environment.CurrentDirectory, "DataForProgram", "Settings.json");
                if (File.Exists(cwdLegacy))
                {
                    fileToLoad = cwdLegacy;
                    Log.Information("Настройки не найдены в AppData, загружаются из: {CwdLegacy}", cwdLegacy);
                }
                else
                {
                    Settings = new AppSettings();
                    Save();
                    return;
                }
            }
        }

        try
        {
            string json = File.ReadAllText(fileToLoad);

            Settings = JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();

            if (Settings.WordReplacements != null)
            {
                Settings.WordReplacements = new Dictionary<string, string>(
                    Settings.WordReplacements,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Settings.WordReplacements = new(StringComparer.OrdinalIgnoreCase);
            }

            if (Settings.CharWordReplacements != null)
            {
                Settings.CharWordReplacements = new Dictionary<string, string>(
                    Settings.CharWordReplacements,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Settings.CharWordReplacements = new(StringComparer.OrdinalIgnoreCase);
            }

            if (Settings.ListBoxItemSourceBlackList != null)
            {
                Settings.ListBoxItemSourceBlackList = new HashSet<string>(
                    Settings.ListBoxItemSourceBlackList,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Settings.ListBoxItemSourceBlackList = new(StringComparer.OrdinalIgnoreCase);
            }

            if (Settings.MasterVolumeMultiplier <= 0f)
            {
                Settings.MasterVolumeMultiplier = 1.0f;
            }

            // Если настройки загружены из старого файла, сразу сохраняем их в AppData
            if (fileToLoad != _settingsFile)
            {
                Save();
                Log.Information("Настройки успешно мигрированы и сохранены в AppData: {SettingsFile}", _settingsFile);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка загрузки настроек");

            Settings = new AppSettings();
        }
    }

    public class AppSettings
    {
        public string? TwitchNick { get; set; }
        public string GoodgameNick { get; set; }
        public string? NttsApi { get; set; }
        public string NttsVoiceChangeCommand { get; set; } = "";
        public bool CheckboxConnectToTwitch { get; set; }
        public bool CheckboxConnectToGoodgame { get; set; }
        public string? SelectedSingleVoice { get; set; }
        public bool RemoveEmoji { get; set; }
        public bool IndividualVoicesEnabled { get; set; }
        public bool CheckBoxNormalizationTts { get; set; }
        public bool CheckBoxAllowSkipMessages { get; set; }
        public string? SkipMessage { get; set; }
        public string? SkipAllMessages { get; set; }
        public string? DoNotTtsIfStartWith { get; set; }
        public int StandardVoiceVolume { get; set; }
        public int StandardVoiceSpeed { get; set; }
        public int CompressorPower { get; set; }
        public float MasterVolumeMultiplier { get; set; } = 1.0f;
        public bool CheckboxConnectToBluestar { get; set; }
        public string? BluestarApi { get; set; }
        public HashSet<string> ListBoxItemSourceBlackList { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> WordReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CharWordReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IndividualVoiceSettings> IndividualVoices { get; set; } = new();
        public bool CheckBoxNttsVoicesIsEnabled { get; set; }
        public bool NttsUseFallbackServer { get; set; }
        public bool TtsDelayEnabled { get; set; }
        public int TtsDelayTime { get; set; }
    }
}