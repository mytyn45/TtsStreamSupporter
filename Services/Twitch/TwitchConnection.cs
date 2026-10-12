using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TtsStreamSupporter.Services;

public class TwitchConnection : ConnectionBase
{
    private const string TwitchIrcHost = "irc.chat.twitch.tv";
    private const int TwitchIrcSslPort = 6697;
    private const int TwitchIrcPlainPort = 6667;

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string? _channelName;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _lifecycleTask;
    private volatile bool _shouldReconnect;
    private DateTime _lastMessageReceivedUtc = DateTime.UtcNow;
    public static TwitchConnection? Instance { get; private set; }

    public TwitchConnection()
    {
        Instance = this;
    }

    public event Action<ChatMessageReceived>? MessageReceived;

    public async Task ReconnectAsync(string? channelName)
    {
        Disconnect();
        await ConnectAsync(channelName);
    }

    public async Task ConnectAsync(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            Log.Warning("Не указан ник канала Twitch для подключения.");
            SetState(ConnectionState.Failed);
            return;
        }

        string normalizedChannel = channelName
            .Trim()
            .TrimStart('#')
            .ToLowerInvariant();

        if (State == ConnectionState.Connected && _channelName == normalizedChannel)
        {
            Log.Information("Twitch уже подключён к #{Channel}", _channelName);
            return;
        }

        // Завершаем предыдущее подключение, если оно было
        Disconnect();

        _channelName = normalizedChannel;
        _shouldReconnect = true;
        _cts = new CancellationTokenSource();

        SetState(ConnectionState.Connecting);

        // Пытаемся подключиться один раз сразу
        bool initialConnected = await TryConnectInternalAsync(_cts.Token);
        if (initialConnected)
        {
            SetState(ConnectionState.Connected);
            Log.Information("Подключение к Twitch #{Channel} завершено.", _channelName);
        }
        else
        {
            SetState(ConnectionState.Connecting);
            Log.Warning("Не удалось сразу подключиться к Twitch #{Channel}. Запуск автоматического переподключения в фоне...", _channelName);
        }

        _lifecycleTask = Task.Run(() => ConnectionLifecycleAsync(_cts.Token));
    }

    public void Disconnect()
    {
        _shouldReconnect = false;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
        }

        _cts = null;

        CloseSocketAndStreams();
        SetState(ConnectionState.Disconnected);

        Log.Information("Отключились от Twitch.");
    }

    private async Task ConnectionLifecycleAsync(CancellationToken ct)
    {
        int reconnectDelaySec = 2;
        const int maxReconnectDelaySec = 30;

        while (_shouldReconnect && !ct.IsCancellationRequested)
        {
            try
            {
                // 1. Если не подключены — пытаемся подключиться с повторами
                while (_shouldReconnect && !ct.IsCancellationRequested && (_client == null || !_client.Connected || _reader == null))
                {
                    SetState(ConnectionState.Connecting);
                    Log.Information("Попытка переподключения к Twitch #{Channel} через {Delay} сек...", _channelName, reconnectDelaySec);

                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySec), ct);

                    if (!_shouldReconnect || ct.IsCancellationRequested)
                        break;

                    bool ok = await TryConnectInternalAsync(ct);
                    if (ok)
                    {
                        reconnectDelaySec = 2;
                        SetState(ConnectionState.Connected);
                        Log.Information("Успешно переподключились к Twitch #{Channel}.", _channelName);
                        break;
                    }

                    reconnectDelaySec = Math.Min(reconnectDelaySec * 2, maxReconnectDelaySec);
                }

                if (!_shouldReconnect || ct.IsCancellationRequested)
                    break;

                // 2. Мы подключены — запускаем мониторинг heartbeat и читаем сообщения
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeatTask = Task.Run(() => HeartbeatLoopAsync(sessionCts), sessionCts.Token);

                try
                {
                    await ReadLoopAsync(sessionCts.Token);
                }
                finally
                {
                    sessionCts.Cancel();
                    try { await heartbeatTask; } catch { }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка в цикле подключения Twitch");
            }
            finally
            {
                CloseSocketAndStreams();
            }

            if (_shouldReconnect && !ct.IsCancellationRequested)
            {
                SetState(ConnectionState.Connecting);
                Log.Warning("Соединение с Twitch #{Channel} потеряно. Подготовка к переподключению...", _channelName);
            }
        }

        CloseSocketAndStreams();
        if (State != ConnectionState.Disconnected)
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        _lastMessageReceivedUtc = DateTime.UtcNow;

        while (_shouldReconnect && !ct.IsCancellationRequested && _reader != null)
        {
            string? message;
            try
            {
                message = await _reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning("Ошибка при чтении из сокета Twitch #{Channel}: {Error}", _channelName, ex.Message);
                break;
            }

            if (message == null)
            {
                Log.Warning("Соединение с Twitch #{Channel} закрыто сервером.", _channelName);
                break;
            }

            _lastMessageReceivedUtc = DateTime.UtcNow;

            if (message.StartsWith("PING"))
            {
                string pongArg = message.Length > 5 ? message[5..] : ":tmi.twitch.tv";
                await SendRawLineAsync($"PONG {pongArg}", ct);
                continue;
            }

            if (message.StartsWith(":tmi.twitch.tv RECONNECT") || message.Equals("RECONNECT", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Сервер Twitch запросил RECONNECT. Переподключение...");
                break;
            }

            if (message.Contains(" NOTICE "))
            {
                Log.Warning("Twitch NOTICE #{Channel}: {Message}", _channelName, message);
            }

            if (message.Contains(" PRIVMSG "))
            {
                ParseMessage(message);
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationTokenSource sessionCts)
    {
        var ct = sessionCts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                var elapsed = DateTime.UtcNow - _lastMessageReceivedUtc;

                // Если уже более 60 секунд нет сообщений, отправляем PING серверу
                if (elapsed >= TimeSpan.FromSeconds(60))
                {
                    await SendRawLineAsync("PING :tmi.twitch.tv", ct);
                }

                // Если прошло более 90 секунд и сервер так и не ответил — сокет завис
                if (elapsed >= TimeSpan.FromSeconds(90))
                {
                    Log.Warning("Таймаут соединения с Twitch #{Channel} (нет активности более 90 сек). Принудительный сброс сокета...", _channelName);
                    try { sessionCts.Cancel(); } catch { }
                    CloseSocketAndStreams();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning("Ошибка в heartbeat Twitch: {Error}", ex.Message);
                try { sessionCts.Cancel(); } catch { }
                CloseSocketAndStreams();
                break;
            }
        }
    }

    private async Task SendRawLineAsync(string line, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_writer != null)
            {
                await _writer.WriteLineAsync(line.AsMemory(), ct);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> TryConnectInternalAsync(CancellationToken ct)
    {
        CloseSocketAndStreams();

        int[] portsToTry = { TwitchIrcSslPort, TwitchIrcPlainPort };

        foreach (int port in portsToTry)
        {
            if (ct.IsCancellationRequested)
                return false;

            try
            {
                var client = new TcpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                try
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
                }
                catch { }

                await client.ConnectAsync(TwitchIrcHost, port, ct);

                Stream stream = client.GetStream();
                if (port == TwitchIrcSslPort)
                {
                    var sslStream = new SslStream(stream, false);
                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = TwitchIrcHost
                    }, ct);
                    stream = sslStream;
                }

                var reader = new StreamReader(stream, Encoding.UTF8);
                var writer = new StreamWriter(stream, Encoding.UTF8)
                {
                    NewLine = "\r\n",
                    AutoFlush = true
                };

                string anonymousNick = $"justinfan{Random.Shared.Next(10000, 99999)}";

                await _writeLock.WaitAsync(ct);
                try
                {
                    await writer.WriteLineAsync("PASS SCHMOOPIIE".AsMemory(), ct);
                    await writer.WriteLineAsync($"NICK {anonymousNick}".AsMemory(), ct);
                    await writer.WriteLineAsync($"JOIN #{_channelName}".AsMemory(), ct);
                }
                finally
                {
                    _writeLock.Release();
                }

                _client = client;
                _reader = reader;
                _writer = writer;
                _lastMessageReceivedUtc = DateTime.UtcNow;

                Log.Information("Успешно подключились к IRC Twitch #{Channel} через порт {Port}{Ssl}",
                    _channelName, port, port == TwitchIrcSslPort ? " (SSL)" : "");
                return true;
            }
            catch (OperationCanceledException)
            {
                CloseSocketAndStreams();
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("Не удалось подключиться к Twitch #{Channel} через порт {Port}: {Error}",
                    _channelName, port, ex.Message);
                CloseSocketAndStreams();
            }
        }

        return false;
    }

    private void CloseSocketAndStreams()
    {
        try { _client?.Client?.Close(0); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _client?.Close(); _client?.Dispose(); } catch { }

        _writer = null;
        _reader = null;
        _client = null;
    }

    private void ParseMessage(string message)
    {
        int privMsg = message.IndexOf(" PRIVMSG ");
        if (privMsg == -1)
        {
            return;
        }

        int messageStart = message.IndexOf(" :", privMsg);
        if (messageStart == -1)
        {
            return;
        }

        string prefix = message[..privMsg];
        int exclamationMark = prefix.IndexOf('!');
        if (exclamationMark == -1)
        {
            return;
        }

        int lastColon = prefix.LastIndexOf(':', exclamationMark);
        int nickStart = lastColon != -1 ? lastColon + 1 : 0;
        if (nickStart >= exclamationMark)
        {
            return;
        }

        string username = prefix[nickStart..exclamationMark];
        string text = message.Substring(messageStart + 2);

        try
        {
            MessageReceived?.Invoke(
                new ChatMessageReceived
                {
                    Username = username,
                    Message = text,
                    Platform = "Twitch"
                });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка обработчика MessageReceived");
        }
    }
}