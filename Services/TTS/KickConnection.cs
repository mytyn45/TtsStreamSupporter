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
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private DateTime _lastMessageReceivedUtc = DateTime.MinValue;

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

    public override async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        var ws = _webSocket;
        if (State != ConnectionState.Connected || ws == null || ws.State != WebSocketState.Open)
        {
            return false;
        }

        var elapsed = DateTime.UtcNow - _lastMessageReceivedUtc;
        if (elapsed < TimeSpan.FromSeconds(30))
        {
            return true;
        }

        var lastReceivedBeforePing = _lastMessageReceivedUtc;
        try
        {
            await SendWebSocketTextAsync(ws, "{\"event\":\"pusher:ping\",\"data\":{}}", ct);

            var startWait = DateTime.UtcNow;
            while (DateTime.UtcNow - startWait < TimeSpan.FromSeconds(5))
            {
                if (ct.IsCancellationRequested || ws.State != WebSocketState.Open)
                    return false;

                if (_lastMessageReceivedUtc > lastReceivedBeforePing)
                {
                    return true;
                }

                await Task.Delay(250, ct);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Warning("Ошибка при проверке связи с Kick: {Error}", ex.Message);
            return false;
        }
    }

    private async Task SendWebSocketTextAsync(ClientWebSocket socket, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(ct);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    ct);
            }
        }
        finally
        {
            _sendLock.Release();
        }
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
            Log.Error("[Kick] Не указан ник канала Kick");
            SetState(ConnectionState.Failed);
            return;
        }

        Disconnect();
        _manualDisconnect = false;

        string slug = CleanSlug(channelToConnect);
        _channelName = slug;
        _cts = new CancellationTokenSource();
        Log.Information("[Kick] Запуск подключения к каналу '{Channel}' (slug: {Slug})...", channelToConnect, slug);

        try
        {
            Log.Information("[Kick] Запрос информации о канале {Slug}...", slug);
            var channelInfo = await GetChannelInfoAsync(slug, _cts.Token);
            if (channelInfo == null)
            {
                Log.Error("[Kick] Не удалось получить информацию о канале Kick {Channel}", slug);
                SetState(ConnectionState.Failed);
                return;
            }

            var (channelId, chatroomId) = channelInfo.Value;
            Log.Information("[Kick] Метаданные получены: Channel ID = {ChannelId}, Chatroom ID = {ChatroomId}", channelId, chatroomId);

            var (appKey, cluster) = await GetPusherCredentialsAsync(channelId, _cts.Token);
            Log.Information("[Kick] Параметры Pusher: Cluster={Cluster}, AppKey={AppKey}", cluster, appKey);

            string wsUrl = $"wss://ws-{cluster}.pusher.com/app/{appKey}?protocol=7&client=js&version=7.4.0&flash=false";
            var uri = new Uri(wsUrl);

            var socket = new ClientWebSocket();
            Log.Information("[Kick] Подключение к WebSocket {Url}...", wsUrl);
            await socket.ConnectAsync(uri, _cts.Token);
            Log.Information("[Kick] WebSocket открыт. Отправка подписки на chatrooms.{ChatroomId}.v2...", chatroomId);

            // Подписываемся на события чата chatrooms.{chatroomId}.v2
            string subMessage = $"{{\"event\":\"pusher:subscribe\",\"data\":{{\"auth\":\"\",\"channel\":\"chatrooms.{chatroomId}.v2\"}}}}";
            await SendWebSocketTextAsync(socket, subMessage, _cts.Token);

            // Если channel_id отличается от chatroom_id, подписываемся также на channel_id
            if (!string.Equals(channelId, chatroomId, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[Kick] Дополнительная подписка на chatrooms.{ChannelId}.v2...", channelId);
                string subChannelMessage = $"{{\"event\":\"pusher:subscribe\",\"data\":{{\"auth\":\"\",\"channel\":\"chatrooms.{channelId}.v2\"}}}}";
                await SendWebSocketTextAsync(socket, subChannelMessage, _cts.Token);
            }

            _webSocket = socket;
            _lastMessageReceivedUtc = DateTime.UtcNow;
            SetState(ConnectionState.Connected);
            Log.Information("[Kick] Подключение к Kick {Channel} (ID: {ChannelId}, Chatroom: {ChatroomId}) успешно завершено.", slug, channelId, chatroomId);

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
            Log.Error(ex, "[Kick] Ошибка при подключении к Kick {Channel}", slug);
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
                _lastMessageReceivedUtc = DateTime.UtcNow;
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

        // Ответ Pusher на наш ping
        if (string.Equals(eventName, "pusher:pong", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(eventName, "pusher:connection_established", StringComparison.OrdinalIgnoreCase))
        {
            string? socketId = null;
            try
            {
                var dataToken = jsonObj["data"];
                if (dataToken?.Type == JTokenType.String)
                {
                    var dataObj = JObject.Parse(dataToken.ToString());
                    socketId = dataObj["socket_id"]?.ToString();
                }
            }
            catch { }
            Log.Information("[Kick] Соединение с сервером Pusher подтверждено (Socket ID: {SocketId}).", socketId ?? "ok");
            return;
        }

        if (string.Equals(eventName, "pusher_internal:subscription_succeeded", StringComparison.OrdinalIgnoreCase))
        {
            string? channel = jsonObj["channel"]?.ToString();
            Log.Information("[Kick] Подписка на канал {Channel} подтверждена сервером Kick.", channel ?? "chatrooms");
            return;
        }

        // 1. Обработка Ping от Pusher
        if (string.Equals(eventName, "pusher:ping", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendWebSocketTextAsync(socket, "{\"event\":\"pusher:pong\",\"data\":{}}", ct);
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

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                if (socket.State == WebSocketState.Open)
                {
                    await SendWebSocketTextAsync(socket, "{\"event\":\"pusher:ping\",\"data\":{}}", ct);
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

