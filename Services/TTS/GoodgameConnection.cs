using System;
using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<string, string> _channelIdCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient _httpClient = CreateGoodgameHttpClient();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private string? _channelName;

    public GoodgameConnection()
    {
        Instance = this;
    }

    private static HttpClient CreateGoodgameHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
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

        string channelKey = channelToConnect.Trim();

        try
        {
            string? channelId = null;

            // 1. Если пользователь ввёл числовой ID напрямую (например "12345"), пропускаем HTTP-запрос
            if (long.TryParse(channelKey, out _))
            {
                channelId = channelKey;
            }
            // 2. Проверяем кэш channel_id, чтобы не парсить страницу при каждом подключении
            else if (_channelIdCache.TryGetValue(channelKey, out var cachedId))
            {
                channelId = cachedId;
            }
            else
            {
                string profile = $"https://goodgame.ru/{Uri.EscapeDataString(channelKey)}";
                channelId = await GetChannelPreload(profile);
                if (!string.IsNullOrWhiteSpace(channelId))
                {
                    _channelIdCache[channelKey] = channelId;
                }
            }

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
            Log.Information("Подключение к GoodGame {Channel} (ID: {ChannelId}) завершено.", channelToConnect, channelId);

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
        using var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(bufferToReceive),
                        ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    ms.Write(bufferToReceive, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                string receivedMessage = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    JObject jsonObj = JObject.Parse(receivedMessage);
                    string? messageType = jsonObj["type"]?.ToString();

                    // Сервер GoodGame отправляет heartbeat {"type":"ping"} — отвечаем pong, чтобы соединение не рвалось
                    if (string.Equals(messageType, "ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var pongBytes = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                        await socket.SendAsync(
                            new ArraySegment<byte>(pongBytes),
                            WebSocketMessageType.Text,
                            true,
                            ct);
                        continue;
                    }

                    // Озвучиваем только реальные сообщения чата и донаты
                    if (!string.Equals(messageType, "message", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(messageType, "payment", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? userName = jsonObj["data"]?["user_name"]?.ToString();
                    string? textValue = jsonObj["data"]?["text"]?.ToString();

                    if (!string.IsNullOrEmpty(textValue))
                    {
                        MessageReceived?.Invoke(new ChatMessageReceived
                        {
                            Username = string.IsNullOrWhiteSpace(userName) ? "Аноним" : userName,
                            VoiceName = string.Empty,
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
        try
        {
            var htmlContent = await _httpClient.GetStringAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var scriptNode = doc.DocumentNode.SelectSingleNode(
                "//script[contains(text(), 'ChannelPreload')]");

            if (scriptNode == null)
                return null;

            var scriptText = scriptNode.InnerText.Trim();

            var startIndex = scriptText.IndexOf('{');
            var endIndex = scriptText.LastIndexOf('}');
            if (startIndex < 0 || endIndex <= startIndex)
                return null;

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
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "HTTP ошибка при получении preload страницы GoodGame {Url}", url);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ошибка при парсинге preload страницы GoodGame {Url}", url);
        }

        return null;
    }
}