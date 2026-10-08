using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public static class IndividualVoiceAssignmentStorage
{
    private static readonly string FilePath =
        Path.Combine(
            DirectoryService.AppDataFolder,
            "individual_voice_assignments.json");

    private static readonly string LegacyFilePath =
        Path.Combine(
            AppContext.BaseDirectory,
            "individual_voice_assignments.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<IndividualVoiceAssignment> Load()
    {
        try
        {
            string fileToLoad = FilePath;
            if (!File.Exists(fileToLoad))
            {
                if (File.Exists(LegacyFilePath))
                    fileToLoad = LegacyFilePath;
                else
                    return new List<IndividualVoiceAssignment>();
            }

            var json = File.ReadAllText(fileToLoad);

            if (string.IsNullOrWhiteSpace(json))
                return new List<IndividualVoiceAssignment>();

            var list = JsonSerializer.Deserialize<
                List<IndividualVoiceAssignment>>(
                    json,
                    JsonOptions)
                ?? new List<IndividualVoiceAssignment>();

            if (fileToLoad != FilePath)
            {
                Save(list);
            }

            return list;
        }
        catch (Exception)
        {
            return new List<IndividualVoiceAssignment>();
        }
    }

    public static void Save(
        IEnumerable<IndividualVoiceAssignment> assignments)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(
                assignments,
                JsonOptions);

            File.WriteAllText(
                FilePath,
                json);
        }
        catch (Exception)
        {

        }
    }
}