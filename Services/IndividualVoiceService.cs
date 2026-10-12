using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public class IndividualVoiceService
{
    private readonly Random _random = new();

    private readonly Dictionary<string, string> _assignments;
    private readonly Dictionary<string, string> _nttsAssignments;

    // Список доступных NTTS-спикеров (id, например "papich"), загружается из файла
    private readonly List<string> _nttsVoiceNames = new();
    private readonly NttsSpeakerLookupService _nttsSpeakerLookupService;
    private readonly SettingsService? _settings;

    public ObservableCollection<IndividualVoiceSettings> Voices { get; }
        = new();

    public ObservableCollection<IndividualVoiceAssignment> Assignments { get; }
        = new();

    // Отдельная коллекция привязок именно для NTTS-голосов
    public ObservableCollection<IndividualVoiceAssignment> NttsAssignments { get; }
        = new();

    public IndividualVoiceService(
        NttsSpeakerLookupService nttsSpeakerLookupService,
        SettingsService? settings = null)
    {
        _nttsSpeakerLookupService = nttsSpeakerLookupService;
        _settings = settings;

        var savedAssignments = IndividualVoiceAssignmentStorage.Load();
        _assignments = savedAssignments
            .GroupBy(x => CreateUserKey(x.Username, x.Platform))
            .ToDictionary(g => g.Key, g => g.Last().VoiceName);

        foreach (var assignment in savedAssignments)
            Assignments.Add(assignment);

        // --- NTTS ---
        LoadNttsVoices();

        var savedNttsAssignments = NttsVoiceAssignmentStorage.Load();
        _nttsAssignments = savedNttsAssignments
            .GroupBy(x => CreateUserKey(x.Username, x.Platform))
            .ToDictionary(g => g.Key, g => g.Last().VoiceName);

        foreach (var assignment in savedNttsAssignments)
            NttsAssignments.Add(assignment);
    }

    // ============================================
    // ГОЛОСА
    // ============================================

    public void AddVoice(
        IndividualVoiceSettings voice)
    {
        voice.PropertyChanged += OnVoiceSettingsChanged;

        Voices.Add(voice);
    }

    private void OnVoiceSettingsChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        VoiceSettingsStorage.Save(Voices);
    }

    // ============================================
    // ЗАГРУЗКА NTTS-ГОЛОСОВ ИЗ ФАЙЛА
    // ============================================
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static DateTime _lastNttsDownloadAttemptUtc = DateTime.MinValue;
    private static readonly TimeSpan NttsDownloadCooldown = TimeSpan.FromMinutes(2);

    public static string GetNttsVoicesFilePath()
    {
        return DirectoryService.NttsVoicesFilePath;
    }

    private void LoadNttsVoices()
    {
        _nttsVoiceNames.Clear();

        string? filePath = null;
        if (File.Exists(DirectoryService.NttsVoicesFilePath))
        {
            filePath = DirectoryService.NttsVoicesFilePath;
        }
        else if (File.Exists(DirectoryService.NttsVoicesDataForProgramFilePath))
        {
            filePath = DirectoryService.NttsVoicesDataForProgramFilePath;
        }
        else
        {
            string legacyAppDataUi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TtsStreamSupporter",
                "DataForProgram",
                "NttsVoices.json");
            string legacyBase = Path.Combine(AppContext.BaseDirectory, "DataForProgram", "NttsVoices.json");

            if (File.Exists(legacyAppDataUi))
                filePath = legacyAppDataUi;
            else if (File.Exists(legacyBase))
                filePath = legacyBase;
        }

        if (filePath == null || !File.Exists(filePath))
            return;

        try
        {
            string json = File.ReadAllText(filePath);

            var data = JsonSerializer.Deserialize<NttsVoicesFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (data == null)
                return;

            var allEntries =
                data.Voices.Concat(data.CustomVoices);

            foreach (var entry in allEntries)
            {
                var speakerId = entry.Speakers.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(speakerId))
                    _nttsVoiceNames.Add(speakerId);
            }

            try
            {
                if (!File.Exists(DirectoryService.NttsVoicesFilePath))
                    File.WriteAllText(DirectoryService.NttsVoicesFilePath, json);
                if (!File.Exists(DirectoryService.NttsVoicesDataForProgramFilePath))
                    File.WriteAllText(DirectoryService.NttsVoicesDataForProgramFilePath, json);
            }
            catch { }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось загрузить локальный NttsVoices.json");
            _nttsVoiceNames.Clear();
        }
    }

    private async Task<bool> DownloadNttsVoicesAsync()
    {
        const string url =
            "https://nttsfuckrkn.fdev.team/api/v1/tts/speakers";

        try
        {
            Log.Information("Загрузка списка NTTS-голосов с сервера");

            string json =
                await _httpClient.GetStringAsync(url);

            var data = JsonSerializer.Deserialize<NttsVoicesFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (data == null)
            {
                Log.Warning("Сервер вернул некорректный список NTTS-голосов");
                return false;
            }

            string filePath = DirectoryService.NttsVoicesFilePath;
            string dfpPath = DirectoryService.NttsVoicesDataForProgramFilePath;

            string? directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string? dfpDir = Path.GetDirectoryName(dfpPath);
            if (dfpDir != null && !Directory.Exists(dfpDir))
                Directory.CreateDirectory(dfpDir);

            await File.WriteAllTextAsync(filePath, json);
            try
            {
                await File.WriteAllTextAsync(dfpPath, json);
            }
            catch { }

            _nttsVoiceNames.Clear();

            var allEntries =
                data.Voices.Concat(data.CustomVoices);

            foreach (var entry in allEntries)
            {
                var speakerId = entry.Speakers.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(speakerId))
                    _nttsVoiceNames.Add(speakerId);
            }

            Log.Information(
                "Загружено {Count} NTTS-голосов",
                _nttsVoiceNames.Count);

            return _nttsVoiceNames.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Не удалось загрузить список NTTS-голосов с сервера");

            return false;
        }
    }

    /// <summary>
    /// Перечитать NttsVoices.json заново (например, после обновления файла кнопкой в UI).
    /// </summary>
    public void ReloadNttsVoices()
    {
        LoadNttsVoices();
    }

    // ============================================
    // ПОЛУЧЕНИЕ ГОЛОСА (обычные)
    // ============================================ public async Task<string?> GetNttsVoiceForUserAsync(

    public IndividualVoiceSettings? GetVoiceForUser(string username, string platform)
    {
        string userKey = CreateUserKey(username, platform);

        if (_assignments.TryGetValue(userKey, out string? voiceName))
        {
            var existingVoice = Voices.FirstOrDefault(x => x.Name == voiceName);
            if (existingVoice != null && existingVoice.IsEnabled)
                return existingVoice;

            _assignments.Remove(userKey);

            var oldAssignment = FindAssignment(Assignments, username, platform);
            if (oldAssignment != null)
                Assignments.Remove(oldAssignment);

            SaveAssignments();
        }

        var enabledVoices = Voices.Where(x => x.IsEnabled).ToList();
        if (enabledVoices.Count == 0)
            return null;

        var selectedVoice = enabledVoices[_random.Next(enabledVoices.Count)];
        _assignments[userKey] = selectedVoice.Name;

        var assignment = new IndividualVoiceAssignment
        {
            Username = username,
            Platform = platform,
            VoiceName = selectedVoice.Name
        };

        Assignments.Add(assignment);
        SaveAssignments();

        return selectedVoice;
    }

    // ============================================
    // ПОЛУЧЕНИЕ ГОЛОСА (NTTS)
    // ============================================

    /// <summary>
    /// Возвращает speaker id (например "papich") для NTTS-синтеза.
    /// Если сообщение начинается с команды смены голоса (например "[команда] <голос>") — меняет привязку пользователя
    /// и возвращает null (это команда, а не текст для озвучки).
    /// Если у пользователя ещё нет привязки — выбирает случайный голос
    /// из NttsVoices.json и сохраняет привязку.
    /// </summary>
    public async Task<string?> GetNttsVoiceForUserAsync(
        string username,
        string platform,
        string message)
    {
        string userKey =
            CreateUserKey(username, platform);

        // Команда смены голоса обрабатывается ДО проверки существующей привязки,
        // чтобы работать и для пользователей, у которых ещё нет назначенного голоса.
        //попытка скачать голоса, если файлика с ними нет
        if (_nttsVoiceNames.Count == 0)
        {
            if (DateTime.UtcNow - _lastNttsDownloadAttemptUtc < NttsDownloadCooldown)
            {
                return null;
            }

            _lastNttsDownloadAttemptUtc = DateTime.UtcNow;
            Log.Warning("Локальный список NTTS-голосов пуст. Пробуем загрузить его с сервера.");

            bool loaded = await DownloadNttsVoicesAsync();

            if (!loaded)
                if (!loaded || _nttsVoiceNames.Count == 0)
                {
                    Log.Error("Не удалось получить список NTTS-голосов");
                    return null;
                }
        }

        string? commandPrefix = _settings?.Settings.NttsVoiceChangeCommand?.Trim();

        string trimmedMessage = message.TrimStart();
        if (!string.IsNullOrWhiteSpace(commandPrefix) &&
            trimmedMessage.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase) &&
            (trimmedMessage.Length == commandPrefix.Length || char.IsWhiteSpace(trimmedMessage[commandPrefix.Length])))
        {
            string query = trimmedMessage[commandPrefix.Length..].Trim().Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";

            if (!string.IsNullOrWhiteSpace(query))
            {
                string? speakerId =
                    await _nttsSpeakerLookupService.FindSpeakerIdAsync(query);

                if (speakerId != null)
                {
                    SetNttsVoiceForUser(
                        username,
                        platform,
                        speakerId);

                    Log.Information(
                        "Голос пользователя {User} на {Platform} изменён на {Voice}",
                        username,
                        platform,
                        speakerId);
                }
                else
                {
                    Log.Information(
                        "Команда {Command}: голос '{Query}' не найден (пользователь {User})",
                        commandPrefix,
                        query,
                        username);
                }
            }

            return null; // это была команда, не озвучиваем текст как обычное сообщение
        }

        // Уже есть привязка — проверяем, что голос всё ещё существует в каталоге
        if (_nttsAssignments.TryGetValue(userKey, out string? voiceName))
        {
            if (_nttsVoiceNames.Contains(voiceName))
                return voiceName;

            // Голос пропал из каталога (например, обновился NttsVoices.json)
            _nttsAssignments.Remove(userKey);

            var oldAssignment = FindAssignment(NttsAssignments, username, platform);
            if (oldAssignment != null)
                NttsAssignments.Remove(oldAssignment);

            SaveNttsAssignments();
        }

        // Проверяем, есть ли уже связка платформа + ник в NttsAssignments
        var existingAssignment = FindAssignment(NttsAssignments, username, platform);

        // Если в памяти нет, проверяем напрямую в файле с привязками
        if (existingAssignment == null)
        {
            var saved = NttsVoiceAssignmentStorage.Load();
            var fromFile = FindAssignment(saved, username, platform);
            if (fromFile != null)
            {
                existingAssignment = fromFile;
                NttsAssignments.Add(existingAssignment);
            }
        }

        // Если связка платформа + ник уже есть — дубликат не создаём!
        if (existingAssignment != null)
        {
            if (_nttsVoiceNames.Contains(existingAssignment.VoiceName))
            {
                _nttsAssignments[userKey] = existingAssignment.VoiceName;
                return existingAssignment.VoiceName;
            }

            if (_nttsVoiceNames.Count == 0)
                return null;

            var newVoice = _nttsVoiceNames[_random.Next(_nttsVoiceNames.Count)];
            existingAssignment.VoiceName = newVoice;
            _nttsAssignments[userKey] = newVoice;
            SaveNttsAssignments();
            return newVoice;
        }

        if (_nttsVoiceNames.Count == 0)
            return null;

        var selectedVoiceName = _nttsVoiceNames[_random.Next(_nttsVoiceNames.Count)];
        _nttsAssignments[userKey] = selectedVoiceName;

        NttsAssignments.Add(new IndividualVoiceAssignment
        {
            Username = username,
            Platform = platform,
            VoiceName = selectedVoiceName
        });

        SaveNttsAssignments();
        return selectedVoiceName;
    }

    // ============================================
    // ИЗМЕНЕНИЕ ГОЛОСА (обычные)
    // ============================================

    public void ChangeAssignmentVoice(IndividualVoiceAssignment assignment, string voiceName)
    {
        if (assignment == null || string.IsNullOrWhiteSpace(voiceName))
            return;

        var voice = Voices.FirstOrDefault(x => x.Name == voiceName);
        if (voice == null || !voice.IsEnabled)
            return;

        string userKey = CreateUserKey(assignment.Username, assignment.Platform);
        _assignments[userKey] = voiceName;
        assignment.VoiceName = voiceName;

        SaveAssignments();
    }

    // ============================================
    // ИЗМЕНЕНИЕ ГОЛОСА (NTTS)
    // ============================================

    public void ChangeNttsAssignmentVoice(IndividualVoiceAssignment assignment, string voiceName)
    {
        if (assignment == null || string.IsNullOrWhiteSpace(voiceName) || !_nttsVoiceNames.Contains(voiceName))
            return;

        string userKey = CreateUserKey(assignment.Username, assignment.Platform);
        _nttsAssignments[userKey] = voiceName;
        assignment.VoiceName = voiceName;

        SaveNttsAssignments();
    }

    // ============================================
    // СОХРАНЕНИЕ
    // ============================================

    private void SaveAssignments()
    {
        IndividualVoiceAssignmentStorage.Save(Assignments);
    }

    private void SaveNttsAssignments()
    {
        var distinctAssignments = NttsAssignments
            .GroupBy(x => CreateUserKey(x.Username, x.Platform), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        NttsVoiceAssignmentStorage.Save(distinctAssignments);
    }

    public void ClearAssignments()
    {
        _assignments.Clear();
        Assignments.Clear();
        SaveAssignments();
    }

    public void ClearNttsAssignments()
    {
        _nttsAssignments.Clear();
        NttsAssignments.Clear();
        SaveNttsAssignments();
    }

    // ============================================
    // КЛЮЧ
    // ============================================

    private static string CreateUserKey(
        string username,
        string platform)
    {
        return $"{platform.Trim().ToLowerInvariant()}:{username.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Принудительно устанавливает NTTS-голос пользователю (например, по команде !s).
    /// Создаёт привязку, если её ещё не было, или обновляет существующую.
    /// </summary>
    public void SetNttsVoiceForUser(
        string username,
        string platform,
        string speakerId)
    {
        string userKey = CreateUserKey(username, platform);

        _nttsAssignments[userKey] = speakerId;

        // Если голос пришёл из живого API и его ещё нет в локальном каталоге —
        // добавляем, чтобы GetNttsVoiceForUserAsync() не сбросил привязку при следующем обращении.
        if (!_nttsVoiceNames.Contains(speakerId))
        {
            _nttsVoiceNames.Add(speakerId);
        }

        var existing = FindAssignment(NttsAssignments, username, platform);
        if (existing != null)
        {
            existing.VoiceName = speakerId;
        }
        else
        {
            NttsAssignments.Add(new IndividualVoiceAssignment
            {
                Username = username,
                Platform = platform,
                VoiceName = speakerId
            });
        }

        SaveNttsAssignments();
    }

    private static bool Matches(IndividualVoiceAssignment item, string username, string platform) =>
        string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.Platform, platform, StringComparison.OrdinalIgnoreCase);

    private static IndividualVoiceAssignment? FindAssignment(IEnumerable<IndividualVoiceAssignment> source, string username, string platform) =>
        source.FirstOrDefault(x => Matches(x, username, platform));
}