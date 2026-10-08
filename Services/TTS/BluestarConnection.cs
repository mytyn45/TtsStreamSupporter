using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TtsStreamSupporter.Services;

public class BluestarConnection : ConnectionBase
{
    public static BluestarConnection? Instance { get; private set; }

    public event Action<ChatMessageReceived>? MessageReceived;

    private string? _token;
    private string? _lastMessageId;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _lifecycleTask;
    private int _reconnectDelayMs = 1000;

    public BluestarConnection()
    {
        Instance = this;
    }

    public async Task ReconnectAsync(string? token)
    {
        Disconnect();
        await ConnectAsync(token);
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
            if (_socket != null)
            {
                _socket.Abort();
                _socket.Dispose();
            }
        }
        catch
        {
        }
        _socket = null;

        SetState(ConnectionState.Disconnected);
        Log.Information("Отключились от Bluestar.");
    }

    public Task StopAsync()
    {
        Disconnect();
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(string? token)
    {
        SetState(ConnectionState.Connecting);

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Error("Не указан токен Bluestar");
            SetState(ConnectionState.Failed);
            return;
        }

        Disconnect();

        _token = token;
        _cts = new CancellationTokenSource();
        _reconnectDelayMs = 1000;

        SetState(ConnectionState.Connecting);

        try
        {
            await OpenSocketAsync(_cts.Token);
            _reconnectDelayMs = 3000;
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected);
            return;
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Connecting);
            Log.Warning("Не удалось сразу подключиться к Bluestar. Запуск фонового цикла переподключения: {Error}", ex.Message);
        }

        _lifecycleTask = Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_socket == null || _socket.State != WebSocketState.Open)
                {
                    await OpenSocketAsync(ct);
                    _reconnectDelayMs = 3000;
                }

                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Failed);
                Log.Error(ex, "Ошибка в цикле Bluestar");
            }

            if (ct.IsCancellationRequested)
                break;

            Log.Information("Bluestar: переподключение через {Delay}мс", _reconnectDelayMs);

            try
            {
                await Task.Delay(_reconnectDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, 30000);
        }

        if (State != ConnectionState.Disconnected)
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private async Task OpenSocketAsync(CancellationToken ct)
    {
        try { _socket?.Dispose(); } catch { }
        _socket = new ClientWebSocket();

        var uri = new Uri($"wss://twitch.catgc.com/api/v1/tts/ws?token={_token}");

        Log.Information("Bluestar: подключение...");

        await _socket.ConnectAsync(uri, ct);

        SetState(ConnectionState.Connected);

        Log.Information("Bluestar: подключено");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        while (_socket != null && _socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();

            WebSocketReceiveResult result;

            do
            {
                result = await _socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    SetState(ConnectionState.Disconnected);
                    return;
                }

                ms.Write(buffer, 0, result.Count);

            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(ms.ToArray());

            HandleRawMessage(json);
        }
    }

    private void HandleRawMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<BluestarMessage>(json);

            if (message == null)
                return;

            switch (message.Type)
            {
                case "tts.ready":
                    Log.Information("Bluestar: подключено к каналу {Channel}", message.Channel);
                    break;

                case "tts.message":
                    _lastMessageId = message.MessageId;

                    MessageReceived?.Invoke(new ChatMessageReceived
                    {
                        Username = message.DisplayName ?? "",
                        VoiceName = null,
                        Message = message.Body ?? "",
                        Platform = "twitch"
                    });
                    break;

                case "error":
                    Log.Error(
                        "Bluestar API error: {Code} | {Message}",
                        message.ErrorCode,
                        message.ErrorMessage);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bluestar: ошибка разбора JSON");
        }
    }
}

public class BluestarMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string? ErrorMessage { get; set; }
}