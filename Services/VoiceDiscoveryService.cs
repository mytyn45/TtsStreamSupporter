using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using Serilog;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public interface IVoiceDiscoveryService
{
    IReadOnlyList<string> GetAvailableVoices();
    IReadOnlyList<string> GetSystemVoices();
    IReadOnlyList<string> GetPiperModelNames();
}

public class VoiceDiscoveryService : IVoiceDiscoveryService
{
    public IReadOnlyList<string> GetSystemVoices()
    {
        var result = new List<string>();
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (InstalledVoice voice in synth.GetInstalledVoices())
            {
                if (voice.Enabled)
                {
                    result.Add(voice.VoiceInfo.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось получить системные голоса Windows");
        }

        return result;
    }

    public IReadOnlyList<string> GetPiperModelNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Проверяем AppData/models
        string appDataModels = Path.Combine(DirectoryService.AppDataFolder, "models");
        AddFromDirectory(appDataModels, result);

        // Проверяем локальную папку models (рядом с exe)
        string localModels = Path.Combine(AppContext.BaseDirectory, "models");
        AddFromDirectory(localModels, result);

        return result.ToList();
    }

    private static void AddFromDirectory(string path, HashSet<string> result)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var folderName = Path.GetFileName(dir);
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    result.Add(folderName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ошибка при чтении каталога моделей: {Path}", path);
        }
    }

    public IReadOnlyList<string> GetAvailableVoices()
    {
        var all = new List<string>();
        all.AddRange(GetSystemVoices());
        foreach (var piper in GetPiperModelNames())
        {
            if (!all.Contains(piper, StringComparer.OrdinalIgnoreCase))
            {
                all.Add(piper);
            }
        }
        return all;
    }
}
