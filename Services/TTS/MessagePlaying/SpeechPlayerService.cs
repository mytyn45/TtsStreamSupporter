using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using NAudio.Wave;
using Serilog;

namespace TtsStreamSupporter.Services;

public class SpeechPlayerService : IDisposable
{
    // Ленивая инициализация: на Linux/macOS не будет падать при старте приложения
    private SpeechSynthesizer? _speech;
    private readonly SettingsService _settings;

    private readonly object _speechLock = new();
    private readonly object _playbackLock = new();

    private WaveOutEvent? _waveOut;

    // Статические блокировки и лимитер: защищают от параллельных запросов
    // даже если создано несколько экземпляров сервиса
    private static readonly AsyncRateLimiter _nttsRateLimiter = new();
    private static readonly SemaphoreSlim _nttsRequestLock = new(1, 1);

    private const int FallbackSuccessesBeforeSwitch = 2;
    private readonly object _nttsServerLock = new();
    private int _activeServerIndex = 0;
    private int _consecutiveFallbackSuccesses = 0;

    private static readonly Regex PiperVoiceRegex = new(@"^[a-z]{2}_[A-Z]{2}-", RegexOptions.Compiled);

    public event Action<bool>? ActiveServerChanged;

    public bool UseFallbackServer
    {
        get
        {
            lock (_nttsServerLock)
            {
                return _activeServerIndex == 1;
            }
        }
        set
        {
            bool changed = false;
            lock (_nttsServerLock)
            {
                int newIndex = value ? 1 : 0;
                if (_activeServerIndex != newIndex)
                {
                    _activeServerIndex = newIndex;
                    _consecutiveFallbackSuccesses = 0;
                    _settings.Settings.NttsUseFallbackServer = value;
                    _ = _settings.DelayedSave();
                    changed = true;
                    Log.Information("NTTS: вручную выбран {Server}", value ? "запасной сервер (ntts.fdev.team)" : "основной сервер (nttsfuckrkn.fdev.team)");
                }
            }

            // Вызываем событие ВНЕ lock во избежание взаимных блокировок (Deadlock)
            if (changed)
            {
                ActiveServerChanged?.Invoke(value);
            }
        }
    }

    private static readonly HttpClient[] _nttsHttpClients =
    [
        new()
        {
            BaseAddress = new Uri("https://nttsfuckrkn.fdev.team"),
            Timeout = TimeSpan.FromSeconds(15)
        },
        new()
        {
            BaseAddress = new Uri("https://ntts.fdev.team"),
            Timeout = TimeSpan.FromSeconds(15)
        }
    ];

    public SpeechPlayerService(SettingsService settings)
    {
        _settings = settings;
        _activeServerIndex = _settings.Settings.NttsUseFallbackServer ? 1 : 0;
    }

    /// <summary>
    /// Генерирует WAV, но НЕ начинает его воспроизводить.
    /// </summary>
    public async Task<PreparedAudio> GenerateAsync(ProcessedTtsMessage message)
    {
        try
        {
            MemoryStream wav;

            if (message.Engine == TtsEngine.Ntts)
            {
                Log.Debug("Генерация NTTS для сообщения: {Text}", message.Text);
                wav = await GenerateWavNttsEngine(message);
            }
            else if (IsPiperVoice(message.VoiceName))
            {
                Log.Debug("Генерация Piper для сообщения: {Text}", message.Text);
                wav = await GenerateWavPiperEngine(message);
            }
            else
            {
                Log.Debug("Генерация SAPI сообщение: {Text}", message.Text);
                // SpeechSynthesizer.Speak() синхронный, поэтому не блокируем поток очереди
                wav = await Task.Run(() => GenerateWavMicrosoftEngine(message));
            }

            wav.Position = 0;
            return new PreparedAudio(wav);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка генерации TTS для сообщения: {Text}", message.Text);
            throw;
        }
    }

    private MemoryStream GenerateWavMicrosoftEngine(ProcessedTtsMessage message)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Microsoft Speech Synthesizer доступен только в Windows.");
        }

        lock (_speechLock)
        {
            _speech ??= new SpeechSynthesizer();

            // Безопасный выбор голоса
            if (!string.IsNullOrWhiteSpace(message.VoiceName))
            {
                bool voiceExists = _speech.GetInstalledVoices()
                    .Any(v => v.Enabled && string.Equals(v.VoiceInfo.Name, message.VoiceName, StringComparison.OrdinalIgnoreCase));

                if (voiceExists)
                {
                    _speech.SelectVoice(message.VoiceName);
                }
                else
                {
                    Log.Warning("Голос SAPI '{Voice}' не найден в системе, используется стандартный голос.", message.VoiceName);
                }
            }

            _speech.Volume = Math.Clamp(message.VoiceVolume, 0, 100);
            _speech.Rate = Math.Clamp(message.VoiceSpeed, -10, 10);

            var stream = new MemoryStream();
            _speech.SetOutputToWaveStream(stream);

            try
            {
                _speech.Speak(message.Text);
            }
            finally
            {
                _speech.SetOutputToNull();
            }

            stream.Position = 0;
            return stream;
        }
    }

    private async Task<MemoryStream> GenerateWavPiperEngine(ProcessedTtsMessage message)
    {
        var stream = await PiperSharpTTS.GenerateVoice(message);
        return stream ?? throw new InvalidOperationException($"Не удалось синтезировать аудио Piper для голоса {message.VoiceName}");
    }

    /// <summary>
    /// Синтезирует речь через удалённый NTTS API и возвращает WAV в памяти.
    /// </summary>
    private async Task<MemoryStream> GenerateWavNttsEngine(ProcessedTtsMessage message)
    {
        string? token = _settings.Settings.NttsApi;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("NTTS API токен не задан в настройках.");
        }

        // Сериализуем запросы к NTTS, чтобы предотвратить параллельные HTTP-запросы
        await _nttsRequestLock.WaitAsync();
        try
        {
            var minInterval = _settings.Settings.TtsDelayEnabled
                ? TimeSpan.FromSeconds(Math.Max(0.5, _settings.Settings.TtsDelayTime))
                : TimeSpan.FromMilliseconds(500);

            await _nttsRateLimiter.WaitTurnAsync(minInterval);

            HttpClient primaryClient;
            HttpClient backupClient;
            int primaryIndex;
            int backupIndex;

            lock (_nttsServerLock)
            {
                primaryIndex = _activeServerIndex;
                backupIndex = 1 - _activeServerIndex;
                primaryClient = _nttsHttpClients[primaryIndex];
                backupClient = _nttsHttpClients[backupIndex];
            }

            string primaryHost = primaryClient.BaseAddress?.ToString() ?? "primary";
            string backupHost = backupClient.BaseAddress?.ToString() ?? "backup";

            // 1. Пробуем текущий активный сервер
            try
            {
                byte[] bytes = await SendNttsRequestWithRetryAsync(primaryClient, message, token);

                lock (_nttsServerLock)
                {
                    if (_activeServerIndex == primaryIndex)
                    {
                        _consecutiveFallbackSuccesses = 0;
                    }
                }

                return new MemoryStream(bytes);
            }
            catch (Exception ex) when (IsClientSideError(ex))
            {
                // Если ошибка 429 TooManyRequests или 401/400, запасной сервер мучить бессмысленно —
                // токен и запрос те же самые. Сразу выбрасываем исключение.
                Log.Error("Запрос к NTTS отклонён сервером ({Host}): {Error}", primaryHost, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning("Основной NTTS-сервер {Host} недоступен или вернул сбой. Ошибка: {Error}", primaryHost, ex.Message);
            }

            // 2. Запасной сервер пробуем ТОЛЬКО при сетевых сбоях или 5xx ошибках основного
            Log.Information("Пробуем запасной NTTS-сервер {Host}...", backupHost);

            bool notifySwitch = false;
            try
            {
                byte[] backupBytes = await SendNttsRequestWithRetryAsync(backupClient, message, token);
                Log.Information("Запасной NTTS-сервер {Host} успешно обработал запрос.", backupHost);

                lock (_nttsServerLock)
                {
                    if (_activeServerIndex == primaryIndex)
                    {
                        _consecutiveFallbackSuccesses++;
                        Log.Information(
                            "Запасной NTTS-сервер ответил при недоступности основного ({Count}/{Max})",
                            _consecutiveFallbackSuccesses,
                            FallbackSuccessesBeforeSwitch);

                        if (_consecutiveFallbackSuccesses >= FallbackSuccessesBeforeSwitch)
                        {
                            _activeServerIndex = backupIndex;
                            _consecutiveFallbackSuccesses = 0;
                            _settings.Settings.NttsUseFallbackServer = (_activeServerIndex == 1);
                            _ = _settings.DelayedSave();
                            notifySwitch = true;

                            Log.Warning(
                                "NTTS: запасной сервер ответил {Max} раза подряд при недоступности основного. Теперь {NewHost} становится основным сервером!",
                                FallbackSuccessesBeforeSwitch,
                                backupHost);
                        }
                    }
                }

                if (notifySwitch)
                {
                    try
                    {
                        ActiveServerChanged?.Invoke(_activeServerIndex == 1);
                    }
                    catch (Exception notifyEx)
                    {
                        Log.Error(notifyEx, "Ошибка при уведомлении ActiveServerChanged");
                    }
                }

                return new MemoryStream(backupBytes);
            }
            catch (Exception backupEx)
            {
                Log.Error(backupEx, "Не удалось получить аудио ни с одного NTTS-сервера ({PrimaryHost}, {BackupHost}).", primaryHost, backupHost);
                throw;
            }
        }
        finally
        {
            _nttsRequestLock.Release();
        }
    }

    private static bool IsClientSideError(Exception ex)
    {
        if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            int code = (int)httpEx.StatusCode.Value;
            // 4xx ошибки (400 Bad Request, 401 Unauthorized, 429 Too Many Requests)
            return code >= 400 && code < 500;
        }
        return false;
    }

    private static async Task<byte[]> SendNttsRequestWithRetryAsync(
        HttpClient client,
        ProcessedTtsMessage message,
        string token)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["speaker"] = message.VoiceName ?? string.Empty;
            query["text"] = message.Text ?? string.Empty;
            query["ext"] = "wav";

            string url = $"/api/v1/tts?{query}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length == 0)
                    {
                        throw new InvalidOperationException("Сервер вернул пустой аудиопоток.");
                    }
                    return bytes;
                }

                string errorBody = await response.Content.ReadAsStringAsync();
                string retryAfterStr = response.Headers.RetryAfter?.ToString() ?? "не указан";

                Log.Warning(
                    "NTTS API вернул HTTP {StatusCode} (попытка {Attempt}/{MaxAttempts}). Сервер: {Server}. " +
                    "Retry-After: {RetryAfter}. Ответ: {Body}",
                    (int)response.StatusCode,
                    attempt,
                    maxAttempts,
                    client.BaseAddress,
                    retryAfterStr,
                    errorBody);

                // Обработка 429 Too Many Requests с уважением к заголовку Retry-After
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
                {
                    TimeSpan delay = TimeSpan.FromSeconds(2.0 * attempt); // Чуть более консервативная пауза

                    if (response.Headers.RetryAfter != null)
                    {
                        if (response.Headers.RetryAfter.Delta.HasValue)
                        {
                            var delta = response.Headers.RetryAfter.Delta.Value;
                            if (delta > TimeSpan.Zero && delta <= TimeSpan.FromSeconds(15))
                            {
                                delay = delta;
                            }
                        }
                        else if (response.Headers.RetryAfter.Date.HasValue)
                        {
                            var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                            if (delta > TimeSpan.Zero && delta <= TimeSpan.FromSeconds(15))
                            {
                                delay = delta;
                            }
                        }
                    }

                    Log.Information("NTTS 429: ожидание {Delay} перед повторной попыткой...", delay);
                    await Task.Delay(delay);
                    continue;
                }

                // Ретраи для временных серверных сбоев (502, 503, 504)
                if (IsTransientServerError(response.StatusCode) && attempt < maxAttempts)
                {
                    TimeSpan delay = TimeSpan.FromSeconds(1.0 * attempt);
                    Log.Information("NTTS {StatusCode}: временная ошибка сервера, повтор через {Delay}...", response.StatusCode, delay);
                    await Task.Delay(delay);
                    continue;
                }

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                // Сетевые сбои (обрыв соединения, таймаут)
                await Task.Delay(TimeSpan.FromSeconds(1.0 * attempt));
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw new HttpRequestException("Не удалось получить аудио от NTTS после всех попыток.");
    }

    private static bool IsTransientServerError(HttpStatusCode code)
    {
        return code is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout;
    }

    /// <summary>
    /// Воспроизводит уже готовый WAV.
    /// </summary>
    public async Task PlayAsync(PreparedAudio audio)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        WaveOutEvent? waveOut = null;
        WaveFileReader? reader = null;

        try
        {
            audio.Wav.Position = 0;
            reader = new WaveFileReader(audio.Wav);

            ISampleProvider chain = reader.ToSampleProvider();
            if (_settings.Settings.CheckBoxNormalizationTts)
            {
                chain = ApplyAudioEffects.ApplyNormalizationIfEnabled(chain, _settings.Settings.CompressorPower);
            }

            chain = ApplyAudioEffects.ApplyVolume(chain, () => _settings.Settings.MasterVolumeMultiplier);

            waveOut = new WaveOutEvent();
            waveOut.Init(chain);

            lock (_playbackLock)
            {
                _waveOut = waveOut;
            }

            var localWaveOut = waveOut;
            var localReader = reader;

            localWaveOut.PlaybackStopped += OnPlaybackStopped;

            void OnPlaybackStopped(object? sender, StoppedEventArgs e)
            {
                localWaveOut.PlaybackStopped -= OnPlaybackStopped;

                localReader.Dispose();
                localWaveOut.Dispose();
                audio.Dispose();

                lock (_playbackLock)
                {
                    if (ReferenceEquals(_waveOut, localWaveOut))
                    {
                        _waveOut = null;
                    }
                }

                if (e.Exception != null)
                {
                    Log.Error(e.Exception, "Ошибка воспроизведения аудио");
                }

                completion.TrySetResult();
            }

            localWaveOut.Play();
        }
        catch
        {
            reader?.Dispose();

            if (waveOut != null)
            {
                lock (_playbackLock)
                {
                    if (ReferenceEquals(_waveOut, waveOut))
                    {
                        _waveOut = null;
                    }
                }
                waveOut.Dispose();
            }

            audio.Dispose();
            throw;
        }

        await completion.Task;
    }

    /// <summary>
    /// Немедленно останавливает текущее воспроизведение.
    /// </summary>
    public void Stop()
    {
        lock (_playbackLock)
        {
            _waveOut?.Stop();
        }
    }

    private static bool IsPiperVoice(string? voiceName)
    {
        return !string.IsNullOrWhiteSpace(voiceName) && PiperVoiceRegex.IsMatch(voiceName);
    }

    public void Dispose()
    {
        lock (_speechLock)
        {
            _speech?.Dispose();
            _speech = null;
        }

        lock (_playbackLock)
        {
            _waveOut?.Dispose();
            _waveOut = null;
        }
    }
}