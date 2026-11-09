using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public class SettingsService
{
    public static string AppDataFolder => DirectoryService.AppDataFolder;

    private readonly string _settingsFile = DirectoryService.SettingsFilePath;
    private readonly string _legacySettingsFile =
        Path.Combine(AppContext.BaseDirectory, "DataForProgram", "Settings.json");

    private CancellationTokenSource? _saveCts;
    private readonly object _lock = new();

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
    }

    public async Task DelayedSave()
    {
        CancellationToken token;
        lock (_lock)
        {
            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();
            token = _saveCts.Token;
        }

        try
        {
            await Task.Delay(1000, token);
            Save();
        }
        catch (OperationCanceledException)
        {
            // Нормальная отмена при повторном изменении настроек в течение задержки
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка отложенного сохранения настроек");
        }
    }

    public void Save()
    {
        lock (_lock)
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
    }

    public void Load()
    {
        lock (_lock)
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
                var json = File.ReadAllText(fileToLoad);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                Settings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();

                if (Settings.ListBoxItemSourceBlackList == null)
                    Settings.ListBoxItemSourceBlackList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Settings.BadWordFilterWhitelist == null)
                    Settings.BadWordFilterWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Settings.WordReplacements == null)
                    Settings.WordReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (Settings.CharWordReplacements == null)
                    Settings.CharWordReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (Settings.IndividualVoices == null)
                    Settings.IndividualVoices = new();

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
    }
}
