using System.Collections.ObjectModel;

namespace TtsStreamSupporter.Settings;

public static class LoadAvailableVoices
{
    private static readonly Services.VoiceDiscoveryService DiscoveryService = new();

    public static void GetListOfAvailableVoices(ObservableCollection<string> comboBoxAvailableVoices)
    {
        comboBoxAvailableVoices.Clear();
        foreach (var voice in DiscoveryService.GetAvailableVoices())
        {
            comboBoxAvailableVoices.Add(voice);
        }
    }
}
