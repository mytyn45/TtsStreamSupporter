// Services/NttsVoiceAssignmentStorage.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public static class NttsVoiceAssignmentStorage
{
    private static string FilePath =>
        Path.Combine(
            DirectoryService.AppDataFolder,
            "NttsVoiceAssignments.json");

    private static string LegacyFilePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "DataForProgram",
            "NttsVoiceAssignments.json");

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

            string json = File.ReadAllText(fileToLoad);

            var list = JsonSerializer.Deserialize<List<IndividualVoiceAssignment>>(json)
                   ?? new List<IndividualVoiceAssignment>();

            if (fileToLoad != FilePath)
            {
                Save(list);
            }

            return list;
        }
        catch
        {
            return new List<IndividualVoiceAssignment>();
        }
    }

    public static void Save(IEnumerable<IndividualVoiceAssignment> assignments)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(assignments, options);

            File.WriteAllText(FilePath, json);
        }
        catch
        {

        }
    }
}