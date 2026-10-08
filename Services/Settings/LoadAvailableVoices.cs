using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;

namespace TtsStreamSupporter.Settings
{
    public static class LoadAvailableVoices
    {
        public static string AppDataFolder =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToltoonTts");
        public static void GetListOfAvailableVoices(
    ObservableCollection<string> comboBoxAvailableVoices)
        {
            using (SpeechSynthesizer synth = new SpeechSynthesizer())
            {
                foreach (InstalledVoice voice in synth.GetInstalledVoices())
                {
                    comboBoxAvailableVoices.Add(voice.VoiceInfo.Name);
                }

                string modelsPath = Path.Combine(AppDataFolder, "models");
                if (Directory.Exists(modelsPath))
                {
                    var folderNames = Directory
                        .GetDirectories(modelsPath)
                        .Select(Path.GetFileName);

                    foreach (string? folder in folderNames)
                    {
                        if (folder != null)
                            comboBoxAvailableVoices.Add(folder);
                    }
                }
            }
        }
    }
}
