namespace TtsStreamSupporter.Services;

public enum TtsEngine
{
    Auto,   // определяется по VoiceName (SAPI/Piper), как было раньше
    Ntts
}

public class ProcessedTtsMessage
{
    public required string Text { get; set; }

    public required string? VoiceName { get; set; }

    public int VoiceVolume { get; set; }

    public int VoiceSpeed { get; set; }

    public string? Platform { get; set; }

    public TtsEngine Engine { get; set; } = TtsEngine.Auto;
}