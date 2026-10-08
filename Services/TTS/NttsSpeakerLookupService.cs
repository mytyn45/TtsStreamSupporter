using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public class NttsSpeakerLookupService
{
    private static readonly string LocalVoicesFilePath =
        Path.Combine(AppContext.BaseDirectory, "dataforprogram", "NttsVoices.json");
    private static string AppDataVoicesFilePath =>
        DirectoryService.NttsVoicesFilePath;

    private static string DataForProgramVoicesFilePath =>
        DirectoryService.NttsVoicesDataForProgramFilePath;

    private static readonly string LegacyVoicesFilePath =
        Path.Combine(AppContext.BaseDirectory, "DataForProgram", "NttsVoices.json");

    private readonly SettingsService _settings;

    private static readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://nttsfuckrkn.fdev.team"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Кэш, чтобы не дёргать API на каждое сообщение в чате
    private List<string>? _cachedSpeakerIds;
    private DateTime _cacheTimestampUtc;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public NttsSpeakerLookupService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Ищет speaker id на сервере NTTS (регистронезависимо).
    /// Возвращает точный id из каталога (например "obama"), либо null, если не найден.
    /// </summary>
    public async Task<string?> FindSpeakerIdAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var speakers = await GetSpeakerIdsAsync();

        return speakers.FirstOrDefault(
            s => string.Equals(s, query, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<string>> GetSpeakerIdsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedSpeakerIds != null &&
                DateTime.UtcNow - _cacheTimestampUtc < _cacheLifetime)
            {
                return _cachedSpeakerIds;
            }

            string json;

            try
            {
                json = await LoadVoicesJsonFromFileAsync();
                json = await FetchVoicesJsonFromServerAsync();
                await SaveVoicesJsonAsync(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // Сайт недоступен — используем локальный резервный файл
                Log.Warning("Не удалось загрузить спикеров NTTS с сервера, загружаем из локального файла");
                json = await LoadVoicesJsonFromFileAsync();
            }

            var data = JsonSerializer.Deserialize<NttsVoicesFile>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var ids = new List<string>();

            if (data != null)
            {
                foreach (var entry in data.Voices.Concat(data.CustomVoices))
                {
                    var speakerId = entry.Speakers.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(speakerId))
                        ids.Add(speakerId);
                }
            }

            _cachedSpeakerIds = ids;
            _cacheTimestampUtc = DateTime.UtcNow;

            return ids;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<string> FetchVoicesJsonFromServerAsync()
    {
        string? token = _settings.Settings.NttsApi;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tts/speakers");

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task SaveVoicesJsonAsync(string json)
    {
        try
        {
            string? dir = Path.GetDirectoryName(AppDataVoicesFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(AppDataVoicesFilePath, json);

            string? dfpDir = Path.GetDirectoryName(DataForProgramVoicesFilePath);
            if (!string.IsNullOrEmpty(dfpDir) && !Directory.Exists(dfpDir))
                Directory.CreateDirectory(dfpDir);

            await File.WriteAllTextAsync(DataForProgramVoicesFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось сохранить NttsVoices.json в AppData");
        }
    }

    private async Task<string> LoadVoicesJsonFromFileAsync()
    {
        string? path = null;
        if (File.Exists(AppDataVoicesFilePath))
            path = AppDataVoicesFilePath;
        else if (File.Exists(DataForProgramVoicesFilePath))
            path = DataForProgramVoicesFilePath;
        else if (File.Exists(LegacyVoicesFilePath))
            path = LegacyVoicesFilePath;

        if (path == null)
        {
            Log.Warning("Локальный файл NttsVoices.json не найден");
            return "{}";
        }

        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Сбросить кэш вручную (например, по кнопке "обновить список" в UI).
    /// </summary>
    public void InvalidateCache()
    {
        _cachedSpeakerIds = null;
    }
}