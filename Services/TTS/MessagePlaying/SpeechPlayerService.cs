using System;
using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using NAudio.Wave;
using Serilog;

namespace TtsStreamSupporter.Services;

public class SpeechPlayerService
{
    private readonly SpeechSynthesizer _speech = new();
    private readonly SettingsService _settings;

    private readonly object _speechLock = new();

    private WaveOutEvent? _waveOut;

    private readonly AsyncRateLimiter _nttsRateLimiter = new();

    private const int FallbackSuccessesBeforeSwitch = 2;
    private readonly object _nttsServerLock = new();
    private int _activeServerIndex = 0;
    private int _consecutiveFallbackSuccesses = 0;

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
            lock (_nttsServerLock)
            {
                int newIndex = value ? 1 : 0;
                if (_activeServerIndex != newIndex)
                {
                    _activeServerIndex = newIndex;
                    _consecutiveFallbackSuccesses = 0;
                    _settings.Settings.NttsUseFallbackServer = value;
                    _ = _settings.DelayedSave();
                    ActiveServerChanged?.Invoke(value);
                    Log.Information("NTTS: вручную выбран {Server}", value ? "запасной сервер (ntts.fdev.team)" : "основной сервер (nttsfuckrkn.fdev.team)");
                }
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
    public async Task<PreparedAudio> GenerateAsync(
        ProcessedTtsMessage message)
    {
        try
        {
            MemoryStream wav;

            if (message.Engine == TtsEngine.Ntts)
            {
                Log.Debug(
                    "Генерация NTTS для сообщения: {Text}",
                    message.Text);

                wav = await GenerateWavNttsEngine(message);
            }
            else if (IsPiperVoice(message.VoiceName))
            {
                Log.Debug(
                    "Генерация Piper для сообщения: {Text}",
                    message.Text);

                wav = await GenerateWavPiperEngine(message);
            }
            else
            {
                Log.Debug(
                    "генерация SAPI сообщение: {Text}",
                    message.Text);

                // SpeechSynthesizer.Speak() синхронный,
                // поэтому не блокируем поток очереди.
                wav = await Task.Run(
                    () => GenerateWavMicrosoftEngine(message));
            }

            wav.Position = 0;

            return new PreparedAudio(wav);
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Ошибка генерации TTS для сообщения: {Text}",
                message.Text);

            throw;
        }
    }

    private MemoryStream GenerateWavMicrosoftEngine(
        ProcessedTtsMessage message)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Microsoft Speech Synthesizer доступен только в Windows.");
        }

        lock (_speechLock)
        {
            _speech.SelectVoice(message.VoiceName);

            _speech.Volume = Math.Clamp(
                message.VoiceVolume,
                0,
                100);

            _speech.Rate = Math.Clamp(
                message.VoiceSpeed,
                -10,
                10);

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

    private async Task<MemoryStream> GenerateWavPiperEngine(
        ProcessedTtsMessage message)
    {
        return await PiperSharpTTS.GenerateVoice(message);
    }

    /// <summary>
    /// Синтезирует речь через удалённый NTTS API и возвращает WAV в памяти.
    /// message.VoiceName должен содержать speaker id (например "papich"),
    /// который выдал IndividualVoiceService.GetNttsVoiceForUser().
    /// </summary>
    private async Task<MemoryStream> GenerateWavNttsEngine(
        ProcessedTtsMessage message)
    {
        string? token = _settings.Settings.NttsApi;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "NTTS API токен не задан в настройках.");
        }

        var minInterval = _settings.Settings.TtsDelayEnabled
            ? TimeSpan.FromSeconds(_settings.Settings.TtsDelayTime)
            : TimeSpan.Zero;

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
            byte[] bytes = await SendNttsRequestAsync(primaryClient, message, token);

            lock (_nttsServerLock)
            {
                if (_activeServerIndex == primaryIndex)
                {
                    _consecutiveFallbackSuccesses = 0;
                }
            }

            return new MemoryStream(bytes);
        }
        catch (Exception ex)
        {
            Log.Warning(
                "Основной NTTS-сервер {Host} не ответил. Ошибка: {Error}",
                primaryHost,
                ex.Message);
        }

        // 2. Если основной сервер промолчал или дал сбой — каждый раз пробуем запасной сервер с тем же запросом
        Log.Information("Пробуем запасной NTTS-сервер {Host}...", backupHost);

        try
        {
            byte[] backupBytes = await SendNttsRequestAsync(backupClient, message, token);

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
                        bool isFallback = _activeServerIndex == 1;
                        _settings.Settings.NttsUseFallbackServer = isFallback;
                        _ = _settings.DelayedSave();

                        Log.Warning(
                            "NTTS: запасной сервер ответил {Max} раза подряд при недоступности основного. Теперь {NewHost} становится основным сервером!",
                            FallbackSuccessesBeforeSwitch,
                            backupHost);

                        try
                        {
                            ActiveServerChanged?.Invoke(isFallback);
                        }
                        catch (Exception notifyEx)
                        {
                            Log.Error(notifyEx, "Ошибка при уведомлении ActiveServerChanged");
                        }
                    }
                }
            }

            return new MemoryStream(backupBytes);
        }
        catch (Exception backupEx)
        {
            Log.Error(
                backupEx,
                "Не удалось получить аудио ни с одного NTTS-сервера ({PrimaryHost}, {BackupHost}).",
                primaryHost,
                backupHost);

            throw;
        }
    }

    private static async Task<byte[]> SendNttsRequestAsync(
        HttpClient client,
        ProcessedTtsMessage message,
        string token)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["speaker"] = message.VoiceName;
        query["text"] = message.Text;
        query["ext"] = "wav";

        string url = $"/api/v1/tts?{query}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes == null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Сервер вернул пустой аудиопоток.");
        }

        return bytes;
    }

    /// <summary>
    /// Воспроизводит уже готовый WAV.
    /// </summary>
    public async Task PlayAsync(PreparedAudio audio)
    {
        var completion =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        WaveOutEvent? waveOut = null;
        WaveFileReader? reader = null;

        try
        {
            audio.Wav.Position = 0;

            reader = new WaveFileReader(audio.Wav);

            ISampleProvider chain =
                reader.ToSampleProvider();
            if (_settings.Settings.CheckBoxNormalizationTts)
            {
                chain = ApplyAudioEffects.ApplyNormalizationIfEnabled(chain, _settings.Settings.CompressorPower);
            }

            chain = ApplyAudioEffects.ApplyVolume(chain, () => _settings.Settings.MasterVolumeMultiplier);

            waveOut = new WaveOutEvent();

            waveOut.Init(chain);

            lock (this)
            {
                _waveOut = waveOut;
            }

            var localWaveOut = waveOut;
            var localReader = reader;

            localWaveOut.PlaybackStopped +=
                OnPlaybackStopped;

            void OnPlaybackStopped(
                object? sender,
                StoppedEventArgs e)
            {
                localWaveOut.PlaybackStopped -=
                    OnPlaybackStopped;

                localReader.Dispose();
                localWaveOut.Dispose();
                audio.Dispose();

                lock (this)
                {
                    if (ReferenceEquals(
                        _waveOut,
                        localWaveOut))
                    {
                        _waveOut = null;
                    }
                }

                if (e.Exception != null)
                {
                    Log.Error(
                        e.Exception,
                        "Ошибка воспроизведения аудио");
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
                lock (this)
                {
                    if (ReferenceEquals(
                        _waveOut,
                        waveOut))
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
        lock (this)
        {
            _waveOut?.Stop();
        }
    }

    private static bool IsPiperVoice(
        string? voiceName)
    {
        return !string.IsNullOrWhiteSpace(voiceName)
               && Regex.IsMatch(
                   voiceName,
                   @"^[a-z]{2}_[A-Z]{2}-");
    }
}