namespace TtsStreamSupporter.Services;

public class ChatMessageReceived
{
    public string Username { get; init; } = "";
    public string VoiceName { get; init; } = "";
    public string Message { get; init; } = "";
    public string Platform { get; init; } = "";
}