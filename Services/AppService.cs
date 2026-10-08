using TtsStreamSupporter.Services;

public class AppServices
{
    public SettingsService Settings { get; }
    public TtsMessageProcessing TtsProcessor { get; }
    public MessageQueue MsgQueue { get; }
    public TwitchConnection TwitchConnection { get; }
    public GoodgameConnection GoodgameConnection { get; }
    public BluestarConnection BluestarConnection { get; }

    public AppServices(
        SettingsService settings,
        TtsMessageProcessing ttsProcessor,
        MessageQueue msgQueue,
        TwitchConnection twitchConnection,
        GoodgameConnection goodgameConnection,
        BluestarConnection bluestarConnection)
    {
        Settings = settings;
        TtsProcessor = ttsProcessor;
        MsgQueue = msgQueue;
        TwitchConnection = twitchConnection;
        GoodgameConnection = goodgameConnection;
        BluestarConnection = bluestarConnection;
    }
}