using CommunityToolkit.Mvvm.ComponentModel;

namespace TtsStreamSupporter.Models;

/// <summary>
/// Настройки одного голоса (SAPI или Piper) для индивидуальных привязок.
/// </summary>
public partial class IndividualVoiceSettings : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private int _speed = 0;

    [ObservableProperty]
    private bool _isEnabled = true;

    public IndividualVoiceSettings()
    {
    }

    public IndividualVoiceSettings(
        string name,
        int volume = 100,
        int speed = 0,
        bool isEnabled = true)
    {
        Name = name;
        Volume = volume;
        Speed = speed;
        IsEnabled = isEnabled;
    }
}
