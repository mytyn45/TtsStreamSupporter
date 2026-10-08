using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TtsStreamSupporter.Models;

/// <summary>
/// Привязка пользователя к голосу. Хранится (Username/Platform/VoiceName) в json;
/// SelectedVoice — только для UI (ComboBox), в json не сохраняется.
/// </summary>
public class IndividualVoiceAssignment : INotifyPropertyChanged
{
    private string _username = "";
    private string _platform = "";
    private string _voiceName = "";
    private IndividualVoiceSettings? _selectedVoice;

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetField(ref _platform, value);
    }

    public string VoiceName
    {
        get => _voiceName;
        set => SetField(ref _voiceName, value);
    }

    /// <summary>
    /// Объект голоса, выбранный в UI (ComboBox). Не сериализуется —
    /// восстанавливается из VoiceName при загрузке (см. InitializeSelectedVoice).
    /// </summary>
    [JsonIgnore]
    public IndividualVoiceSettings? SelectedVoice
    {
        get => _selectedVoice;
        set => SetField(ref _selectedVoice, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
