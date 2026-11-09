using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TtsStreamSupporter.Models;

/// <summary>
/// Привязка пользователя к голосу. Хранится (Username/Platform/VoiceName) в JSON.
/// SelectedVoice — для UI (ComboBox), в JSON не сохраняется.
/// </summary>
public partial class IndividualVoiceAssignment : ObservableObject
{
    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _platform = "";

    [ObservableProperty]
    private string _voiceName = "";

    [JsonIgnore]
    [ObservableProperty]
    private IndividualVoiceSettings? _selectedVoice;
}
