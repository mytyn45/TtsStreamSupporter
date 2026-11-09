using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace TtsStreamSupporter.Services;

public class KickConnection : ConnectionBase
{
    public static KickConnection? Instance { get; private set; }

    public event Action<ChatMessageReceived>? MessageReceived;

    private const string DefaultAppKey = "34bf7a0ff419a2a775b9";
    private const string DefaultCluster = "us2";

    private static readonly ConcurrentDictionary<string, (string ChannelId, string ChatroomId)> _channelCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient _httpClient = CreateHttpClient();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _heartbeatTask;
    private string? _channelName;
    private bool _manualDisconnect;

    public KickConnection()
    {
        Instance = this;
    }

    private static HttpClient CreateHttpClient()
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
        _manualDisconnect = true;

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
        Log.Information("Отключились от Kick.");
    }

    public async Task ConnectAsync(string? channelToConnect)
    {
        SetState(ConnectionState.Connecting);

        if (string.IsNullOrWhiteSpace(channelToConnect))
        {
            Log.Error("Не указан ник канала Kick");
            SetState(ConnectionState.Failed);
            return;
        }

        Disconnect();
        _manualDisconnect = false;

        string slug = CleanSlug(channelToConnect);
        _channelName = slug;
        _cts = new CancellationTokenSource();

        try
        {
            var channelInfo = await GetChannelInfoAsync(slug, _cts.Token);
            if (channelInfo == null)
            {
                Log.Error("Не удалось получить информацию о канале Kick {Channel}", slug);
                SetState(ConnectionState.Failed);
                return;
            }

            var (channelId, chatroomId) = channelInfo.Value;

            var (appKey, cluster) = await GetPusherCredentialsAsync(channelId, _cts.Token);

            string wsUrl = $"wss://ws-{cluster}.pusher.com/app/{appKey}?protocol=7&client=js&version=7.4.0&flash=false";
            var uri = new Uri(wsUrl);

            var socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, _cts.Token);

            // Подписываемся на события чата chatrooms.{chatroomId}.v2
            string subMessage = $"{{\"event\":\"pusher:subscribe\",\"data\":{{\"auth\":\"\",\"channel\":\"chatrooms.{chatroomId}.v2\"}}}}";
            byte[] subBytes = Encoding.UTF8.GetBytes(subMessage);

            await socket.SendAsync(
                new ArraySegment<byte>(subBytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token);

            // Если channel_id отличается от chatroom_id, подписываемся также на channel_id
            if (!string.Equals(channelId, chatroomId, StringComparison.OrdinalIgnoreCase))
            {
                string subChannelMessage = $"{{\"event\":\"pusher:subscribe\",\"data\":{{\"auth\":\"\",\"channel\":\"chatrooms.{channelId}.v2\"}}}}";
                byte[] subChannelBytes = Encoding.UTF8.GetBytes(subChannelMessage);
                await socket.SendAsync(
                    new ArraySegment<byte>(subChannelBytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);
            }

            _webSocket = socket;
            SetState(ConnectionState.Connected);
            Log.Information("Подключение к Kick {Channel} (ID: {ChannelId}, Chatroom: {ChatroomId}) успешно завершено.", slug, channelId, chatroomId);

            _readTask = Task.Run(() => ReceiveLoopAsync(socket, slug, _cts.Token));
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(socket, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Failed);
            Log.Error(ex, "Ошибка при подключении к Kick {Channel}", slug);
            Disconnect();
        }
    }

    private static string CleanSlug(string input)
    {
        string trimmed = input.Trim().TrimEnd('/');
        if (trimmed.Contains('/'))
        {
            trimmed = trimmed.Substring(trimmed.LastIndexOf('/') + 1);
        }
        return trimmed.TrimStart('@');
    }

    private static async Task<(string ChannelId, string ChatroomId)?> GetChannelInfoAsync(string slug, CancellationToken ct)
    {
        if (_channelCache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        try
        {
            string url = $"https://kick.com/api/v2/channels/{Uri.EscapeDataString(slug)}";
            using var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Запрос информации о канале Kick {Channel} вернул HTTP {StatusCode}", slug, (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);

            string? channelId = obj["id"]?.ToString();
            string? chatroomId = obj["chatroom"]?["id"]?.ToString();

            if (string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(chatroomId))
            {
                channelId = chatroomId;
            }
            else if (string.IsNullOrWhiteSpace(chatroomId) && !string.IsNullOrWhiteSpace(channelId))
            {
                chatroomId = channelId;
            }

            if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(chatroomId))
            {
                var result = (channelId, chatroomId);
                _channelCache[slug] = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ошибка при разрешении канала Kick {Channel}", slug);
        }

        return null;
    }

    private static async Task<(string AppKey, string Cluster)> GetPusherCredentialsAsync(string channelId, CancellationToken ct)
    {
        try
        {
            string url = $"https://web.kick.com/api/v1/realtime/channels/{channelId}/chat/connection";
            var body = new
            {
                client = new { id = "toltoontts", type = "web" },
                capabilities = new { accepted_providers = new[] { new { provider = "pusher" } } }
            };

            string jsonBody = JsonConvert.SerializeObject(body);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct);
                var obj = JObject.Parse(json);

                var conn = obj["data"]?["connections"]?[0];
                string? appKey = conn?["credentials"]?["app_key"]?.ToString();
                string? cluster = conn?["credentials"]?["cluster"]?.ToString();

                if (!string.IsNullOrWhiteSpace(appKey) && !string.IsNullOrWhiteSpace(cluster))
                {
                    return (appKey, cluster);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Не удалось получить динамические параметры Pusher для Kick, используем дефолтные.");
        }

        return (DefaultAppKey, DefaultCluster);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, string slug, CancellationToken ct)
    {
        var buffer = new byte[16384];
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
                        new ArraySegment<byte>(buffer),
                        ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                string messageText = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    ProcessPusherMessage(socket, messageText, ct);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Ошибка при обработке сообщения от Kick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Корректная отмена
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка соединения с Kick {Channel}", slug);
            SetState(ConnectionState.Failed);
        }
        finally
        {
            try { socket.Dispose(); } catch { }
        }

        if (!_manualDisconnect && State == ConnectionState.Connected)
        {
            SetState(ConnectionState.Connecting);
            _ = Task.Run(async () =>
            {
                Log.Warning("Соединение с Kick прервано. Попытка переподключения через 3 секунды...");
                await Task.Delay(3000);
                if (!_manualDisconnect)
                {
                    await ConnectAsync(_channelName);
                }
            });
        }
        else if (State != ConnectionState.Disconnected)
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private void ProcessPusherMessage(ClientWebSocket socket, string messageText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        var jsonObj = JObject.Parse(messageText);
        string? eventName = jsonObj["event"]?.ToString();

        // 1. Обработка Ping от Pusher
        if (string.Equals(eventName, "pusher:ping", StringComparison.OrdinalIgnoreCase))
        {
            var pongBytes = Encoding.UTF8.GetBytes("{\"event\":\"pusher:pong\",\"data\":{}}");
            _ = socket.SendAsync(
                new ArraySegment<byte>(pongBytes),
                WebSocketMessageType.Text,
                true,
                ct);
            return;
        }

        // 2. Обработка сообщений чата
        if (string.Equals(eventName, "App\\Events\\ChatMessageEvent", StringComparison.OrdinalIgnoreCase))
        {
            var dataToken = jsonObj["data"];
            if (dataToken == null)
                return;

            JObject? dataObj;
            if (dataToken.Type == JTokenType.String)
            {
                dataObj = JObject.Parse(dataToken.ToString());
            }
            else if (dataToken is JObject obj)
            {
                dataObj = obj;
            }
            else
            {
                return;
            }

            string? content = dataObj["content"]?.ToString();
            string? senderUsername = dataObj["sender"]?["username"]?.ToString()
                ?? dataObj["sender"]?["slug"]?.ToString();

            if (!string.IsNullOrEmpty(content))
            {
                MessageReceived?.Invoke(new ChatMessageReceived
                {
                    Username = string.IsNullOrWhiteSpace(senderUsername) ? "Аноним" : senderUsername,
                    VoiceName = string.Empty,
                    Message = content,
                    Platform = "kick"
                });
            }
        }
    }

    private static async Task HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var pingBytes = Encoding.UTF8.GetBytes("{\"event\":\"pusher:ping\",\"data\":{}}");

        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(
                        new ArraySegment<byte>(pingBytes),
                        WebSocketMessageType.Text,
                        true,
                        ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}

