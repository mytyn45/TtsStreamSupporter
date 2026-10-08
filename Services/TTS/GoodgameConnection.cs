using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace TtsStreamSupporter.Services;

public class GoodgameConnection : ConnectionBase
{
    public static GoodgameConnection? Instance { get; private set; }

    public event Action<ChatMessageReceived>? MessageReceived;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private string? _channelName;

    public GoodgameConnection()
    {
        Instance = this;
    }

    public async Task ReconnectAsync(string? channelToConnect)
    {
        Disconnect();
        await ConnectAsync(channelToConnect);
    }

    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
        }
        _cts = null;

        try
        {
            if (_webSocket != null)
            {
                _webSocket.Abort();
                _webSocket.Dispose();
            }
        }
        catch
        {
        }
        _webSocket = null;

        SetState(ConnectionState.Disconnected);
        Log.Information("Отключились от GoodGame.");
    }

    public async Task ConnectAsync(string? channelToConnect)
    {
        SetState(ConnectionState.Connecting);

        if (string.IsNullOrWhiteSpace(channelToConnect))
        {
            Log.Error("Не указан ник канала Гудгейма");
            SetState(ConnectionState.Failed);
            return;
        }

        Disconnect();

        _channelName = channelToConnect;
        _cts = new CancellationTokenSource();

        string profile = $"https://goodgame.ru/{channelToConnect}";

        try
        {
            var channelId = await GetChannelPreload(profile);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                Log.Error("Не удалось получить channel_id для GoodGame {Channel}", channelToConnect);
                SetState(ConnectionState.Failed);
                return;
            }

            var socket = new ClientWebSocket();
            const string url = "wss://chat.goodgame.ru/chat/websocket";
            var uri = new Uri(url);

            await socket.ConnectAsync(uri, _cts.Token);

            var message = new
            {
                type = "join",
                data = new { channel_id = channelId }
            };

            string jsonMessage = System.Text.Json.JsonSerializer.Serialize(message);
            byte[] bufferToSend = Encoding.UTF8.GetBytes(jsonMessage);

            await socket.SendAsync(
                new ArraySegment<byte>(bufferToSend),
                WebSocketMessageType.Text,
                true,
                _cts.Token);

            _webSocket = socket;
            SetState(ConnectionState.Connected);
            Log.Information("Подключение к GoodGame {Channel} завершено.", channelToConnect);

            _readTask = Task.Run(() => ReceiveLoopAsync(socket, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Failed);
            Log.Error(ex, "Ошибка при подключении к GoodGame {Channel}", channelToConnect);
            Disconnect();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var bufferToReceive = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(bufferToReceive),
                    ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                string receivedMessage = Encoding.UTF8.GetString(bufferToReceive, 0, result.Count);
                try
                {
                    JObject jsonObj = JObject.Parse(receivedMessage);
                    string? userName = jsonObj["data"]?["user_name"]?.ToString();
                    string? textValue = jsonObj["data"]?["text"]?.ToString();

                    if (!string.IsNullOrEmpty(textValue))
                    {
                        MessageReceived?.Invoke(new ChatMessageReceived
                        {
                            Username = userName,
                            VoiceName = null,
                            Message = textValue,
                            Platform = "goodgame"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Ошибка при обработке сообщения от GoodGame");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена соединения
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при получении данных от Гудгейма");
            SetState(ConnectionState.Failed);
            return;
        }
        finally
        {
            try { socket.Dispose(); } catch { }
        }

        if (State != ConnectionState.Disconnected)
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private static async Task<string?> GetChannelPreload(string url)
    {
        using var httpClient = new HttpClient();
        var htmlContent = await httpClient.GetStringAsync(url);

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var scriptNode = doc.DocumentNode.SelectSingleNode(
            "//script[contains(text(), 'ChannelPreload')]");

        if (scriptNode == null)
            return null;

        var scriptText = scriptNode.InnerText.Trim();

        var startIndex = scriptText.IndexOf('{');
        var endIndex = scriptText.LastIndexOf('}');
        var jsonContent = scriptText.Substring(startIndex, endIndex - startIndex + 1);

        using var reader = new JsonTextReader(new StringReader(jsonContent));
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.PropertyName &&
                reader.Value?.ToString() == "channel_id")
            {
                reader.Read();
                return reader.Value?.ToString();
            }
        }

        return null;
    }
}