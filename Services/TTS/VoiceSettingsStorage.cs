using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public static class VoiceSettingsStorage
{
    private static readonly string FilePath =
        Path.Combine(DirectoryService.AppDataFolder, "voice_settings.json");

    private static readonly string LegacyFilePath =
        Path.Combine(AppContext.BaseDirectory, "voice_settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Save(IEnumerable<IndividualVoiceSettings> voices)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dtos = new List<VoiceSettingsDto>();
            foreach (var v in voices)
            {
                dtos.Add(new VoiceSettingsDto
                {
                    Name = v.Name,
                    Volume = v.Volume,
                    Speed = v.Speed,
                    IsEnabled = v.IsEnabled
                });
            }

            var json = JsonSerializer.Serialize(dtos, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {

        }
    }

    public static Dictionary<string, VoiceSettingsDto> Load()
    {
        var result = new Dictionary<string, VoiceSettingsDto>();

        string fileToLoad = FilePath;
        if (!File.Exists(fileToLoad))
        {
            if (File.Exists(LegacyFilePath))
            {
                fileToLoad = LegacyFilePath;
            }
            else
            {
                return result;
            }
        }

        try
        {
            var json = File.ReadAllText(fileToLoad);
            var dtos = JsonSerializer.Deserialize<List<VoiceSettingsDto>>(json, Options);

            if (dtos != null)
            {
                foreach (var dto in dtos)
                    result[dto.Name] = dto;
            }

            if (fileToLoad != FilePath)
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(FilePath, json);
            }
        }
        catch
        {

        }

        return result;
    }
}