using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToltoonTTS2.Services.Blu3star_s_chat;

public class Blu3starConnection : IBlu3starConnection
{
    public enum Blu3starConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    private string _token;

    private string _lastMessageId;

    private Blu3starConnectionState CurrentState {get; set; }

    Blu3starConnectionState IBlu3starConnection.CurrentState => CurrentState;

    public event EventHandler<Blu3starConnectionState> ConnectionStateChanged;

    private ClientWebSocket _socket;

    public event EventHandler<Blu3starMessageEventArgs> MessageReceived;

    private CancellationTokenSource _cts;

    private int _reconnectDelayMs = 1000;

    private void UpdateState(Blu3starConnectionState newState)
    {
        if (CurrentState != newState)
        {
            CurrentState = newState;
            ConnectionStateChanged?.Invoke(this, newState);
        }
    }

    protected virtual void OnMessageReceived(Blu3starMessageEventArgs e)
    {
        var abc = MessageReceived?.GetInvocationList().Length;

        MessageReceived?.Invoke(this, e);
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();

            if (_socket != null)
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
        }
        catch
        {
        }
    }

    private async Task ConnectLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync();

                _reconnectDelayMs = 3000;

                await ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket error: {ex.Message}");
            }

            Console.WriteLine($"Reconnect in {_reconnectDelayMs}ms");

            await Task.Delay(_reconnectDelayMs);

            _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, 30000);
        }
    }

    private async Task ConnectAsync()
    {
        _socket?.Dispose();

        _socket = new ClientWebSocket();

        var uri = new Uri(
            $"wss://twitch.catgc.com/api/v1/tts/ws?token={_token}");

        Console.WriteLine("Connecting...");

        await _socket.ConnectAsync(uri, _cts.Token);

        Console.WriteLine("Connected");
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];

        while (_socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();

            WebSocketReceiveResult result;

            do
            {
                result = await _socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("Socket closed");
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
            var message = JsonSerializer.Deserialize<LiberationMessage>(json);

            if (message == null)
                return;

            switch (message.Type)
            {
                case "tts.ready":
                    Console.WriteLine(
                        $"Connected to channel: {message.Channel}");
                    break;

                case "tts.message":

                    _lastMessageId = message.MessageId;

                    Console.WriteLine(
                        $"[{message.DisplayName}] {message.Body}");

                    OnMessageReceived(new Blu3starMessageEventArgs
                    {
                        UserName = message.DisplayName,
                        Message = message.Body
                    });

                    break;

                case "error":

                    Console.WriteLine(
                        $"API error: {message.ErrorCode} | {message.ErrorMessage}");

                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JSON parse error: {ex}");
        }
    }

    public async Task Blue3starConnect(string Api)
    {
        _token = Api;
        _cts = new CancellationTokenSource();
        await ConnectLoopAsync();
    }
}

public class Blu3starMessageEventArgs : EventArgs
{
    public string UserName { get; set; }
    public string Message { get; set; }
}

public class LiberationMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("channel")]
    public string Channel { get; set; }

    [JsonPropertyName("message_id")]
    public string MessageId { get; set; }

    [JsonPropertyName("user_id")]
    public string UserId { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("code")]
    public string ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string ErrorMessage { get; set; }
}