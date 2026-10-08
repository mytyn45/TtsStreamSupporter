namespace TtsStreamSupporter.Models;

/// <summary>
/// Плоское представление IndividualVoiceSettings для сохранения в voice_settings.json.
/// </summary>
public class VoiceSettingsDto
{
    public string Name { get; set; } = "";

    public int Volume { get; set; } = 100;

    public int Speed { get; set; } = 0;

    public bool IsEnabled { get; set; } = true;
}
