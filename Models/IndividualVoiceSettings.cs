using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TtsStreamSupporter.Models;

/// <summary>
/// Настройки одного голоса (SAPI или Piper) для индивидуальных привязок.
/// </summary>
public class IndividualVoiceSettings : INotifyPropertyChanged
{
    private string _name;
    private int _volume;
    private int _speed;
    private bool _isEnabled;

    public IndividualVoiceSettings(
        string name,
        int volume = 100,
        int speed = 0,
        bool isEnabled = true)
    {
        _name = name;
        _volume = volume;
        _speed = speed;
        _isEnabled = isEnabled;
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int Volume
    {
        get => _volume;
        set => SetField(ref _volume, value);
    }

    public int Speed
    {
        get => _speed;
        set => SetField(ref _speed, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
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
