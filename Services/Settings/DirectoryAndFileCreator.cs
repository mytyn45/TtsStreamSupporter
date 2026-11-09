using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.Settings
{
    public static class DirectoryService
    {
        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToltoonTts");

        public static string DataForProgramFolder =>
            Path.Combine(AppDataFolder, "DataForProgram");

        public static string SettingsFilePath =>
            Path.Combine(AppDataFolder, "Settings.json");

        public static string NttsVoicesFilePath =>
            Path.Combine(AppDataFolder, "NttsVoices.json");

        public static string NttsVoicesDataForProgramFilePath =>
            Path.Combine(DataForProgramFolder, "NttsVoices.json");

        private static string DefaultJsonContent = "[]";
        private readonly static string _cwd = Environment.CurrentDirectory;

        public static void EnsureAppStructureExists()
        {
            Directory.CreateDirectory(AppDataFolder);

            // Если рядом с exe осталась старая папка DataForProgram от предыдущих версий,
            // мигрируем её содержимое в AppData, но новые папки рядом с exe не создаём.
            string localDataForProgram = Path.Combine(_cwd, "DataForProgram");
            if (Directory.Exists(localDataForProgram))
            {
                try
                {
                    CopyDirectoryContentsIfMissing(localDataForProgram, DataForProgramFolder);
                }
                catch { }
            }

            string baseDataForProgram = Path.Combine(AppContext.BaseDirectory, "DataForProgram");
            if (!string.Equals(localDataForProgram, baseDataForProgram, StringComparison.OrdinalIgnoreCase) && Directory.Exists(baseDataForProgram))
            {
                try
                {
                    CopyDirectoryContentsIfMissing(baseDataForProgram, DataForProgramFolder);
                }
                catch { }
            }

            string[] dataFolders = [DataForProgramFolder];
            string[] subFolders = ["BlackList", "Profiles", "WordReplace", "Voices", "SoundEffects"];
            string[] voiceDbs = ["GoodgameIndividualVoices.db", "IndividualVoices.db", "KickIndividualVoices.db", "TwitchIndividualVoices.db", "VkIndividualVoices.db"];

            foreach (var baseDir in dataFolders)
            {
                foreach (var sub in subFolders)
                {
                    CreateFolder(sub, baseDir);
                }

                CreateFile("BlackListUsers.json", Path.Combine(baseDir, "BlackList"));
                CreateFile("WhatToReplace.json", Path.Combine(baseDir, "WordReplace"));
                CreateFile("WhatToReplaceWith.json", Path.Combine(baseDir, "WordReplace"));

                foreach (var db in voiceDbs)
                {
                    CreateFile(db, Path.Combine(baseDir, "Voices"));
                }
            }
            SyncNttsVoices();
            PiperVoicesData.EnsureStructureAndVoicesList();
        }

        private static void CopyDirectoryContentsIfMissing(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
                return;

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(targetDir, rel);
                string? destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (!File.Exists(dest))
                {
                    File.Copy(file, dest, false);
                }
            }
        }

        private static void SyncNttsVoices()
        {
            try
            {
                bool rootExists = File.Exists(NttsVoicesFilePath);
                bool dfpExists = File.Exists(NttsVoicesDataForProgramFilePath);

                if (rootExists && !dfpExists)
                {
                    File.Copy(NttsVoicesFilePath, NttsVoicesDataForProgramFilePath, true);
                }
                else if (!rootExists && dfpExists)
                {
                    File.Copy(NttsVoicesDataForProgramFilePath, NttsVoicesFilePath, true);
                }
                else if (rootExists && dfpExists)
                {
                    var rootInfo = new FileInfo(NttsVoicesFilePath);
                    var dfpInfo = new FileInfo(NttsVoicesDataForProgramFilePath);
                    if (rootInfo.Length > dfpInfo.Length)
                    {
                        File.Copy(NttsVoicesFilePath, NttsVoicesDataForProgramFilePath, true);
                    }
                    else if (dfpInfo.Length > rootInfo.Length)
                    {
                        File.Copy(NttsVoicesDataForProgramFilePath, NttsVoicesFilePath, true);
                    }
                }
            }
            catch { }
        }

        public static void CreateFolder(string folderName, string basePath)
        {
            var fullPath = Path.Combine(basePath, folderName);
            try
            {
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось создать папку: {Path}", fullPath);
            }
        }

        public static void CreateFile(string fileName, string folderPath)
        {
            string fullPath = Path.Combine(folderPath, fileName);
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath, DefaultJsonContent);
            }
        }

        public static void OpenAppDataFolder()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppDataFolder}\"",
                UseShellExecute = true
            });
        }
    }
}
