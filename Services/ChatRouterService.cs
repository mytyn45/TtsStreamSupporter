using System;
using Serilog;
using TtsStreamSupporter.Models;

namespace TtsStreamSupporter.Services;

public interface IChatRouterService : IDisposable
{
    void Initialize();
}

public class ChatRouterService : IChatRouterService
{
    private readonly TtsMessageProcessing _ttsProcessor;
    private readonly MessageQueue _messageQueue;
    private readonly TwitchConnection _twitchConnection;
    private readonly GoodgameConnection _goodgameConnection;
    private readonly KickConnection _kickConnection;
    private readonly BluestarConnection _bluestarConnection;

    private bool _initialized;

    public ChatRouterService(
        TtsMessageProcessing ttsProcessor,
        MessageQueue messageQueue,
        TwitchConnection twitchConnection,
        GoodgameConnection goodgameConnection,
        KickConnection kickConnection,
        BluestarConnection bluestarConnection)
    {
        _ttsProcessor = ttsProcessor;
        _messageQueue = messageQueue;
        _twitchConnection = twitchConnection;
        _goodgameConnection = goodgameConnection;
        _kickConnection = kickConnection;
        _bluestarConnection = bluestarConnection;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        _twitchConnection.MessageReceived += OnChatMessageReceived;
        _goodgameConnection.MessageReceived += OnChatMessageReceived;
        _kickConnection.MessageReceived += OnChatMessageReceived;
        _bluestarConnection.MessageReceived += OnChatMessageReceived;

        Log.Information("Маршрутизатор чатов запущен");
    }

    private async void OnChatMessageReceived(ChatMessageReceived message)
    {
        try
        {
            var result = await _ttsProcessor.ProcessIncomingMessage(
                message.Username,
                message.VoiceName,
                message.Message,
                message.Platform);

            switch (result.Status)
            {
                case TtsProcessStatus.Success:
                    if (result.Message != null)
                    {
                        _messageQueue.Enqueue(result.Message);
                    }
                    break;

                case TtsProcessStatus.Skipped:
                case TtsProcessStatus.Blacklisted:
                case TtsProcessStatus.Filtered:
                    // Штатные сценарии
                    break;

                case TtsProcessStatus.Empty:
                    Log.Debug("Сообщение пришло пустым");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Ошибка при обработке сообщения от {User} ({Platform})",
                message.Username,
                message.Platform);
        }
    }

    public void Dispose()
    {
        _twitchConnection.MessageReceived -= OnChatMessageReceived;
        _goodgameConnection.MessageReceived -= OnChatMessageReceived;
        _kickConnection.MessageReceived -= OnChatMessageReceived;
        _bluestarConnection.MessageReceived -= OnChatMessageReceived;
    }
}
