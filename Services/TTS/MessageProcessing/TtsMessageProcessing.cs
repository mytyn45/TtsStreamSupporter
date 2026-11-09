using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NickBuhro.Translit;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter.Services;

public class TtsMessageProcessing
{
    private readonly SettingsService _settings;
    private readonly IndividualVoiceService _individualVoiceService;
    private readonly MessageQueue _messageQueue;

    // Скомпилированные регулярные выражения для максимальной производительности
    private static readonly Regex MultiDotRegex = new(@"\.{2,}", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(
        @"\b(?:https?:\/\/)?(?:www\.)?(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}(?::\d+)?(?:\/[^\s]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmojiRegex = new(
        @"(?:[\u203C-\u3299\u00A9\u00AE\u2000-\u3300\uF000-\uFFFF]|[\uD800-\uDBFF][\uDC00-\uDFFF])",
        RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public TtsMessageProcessing(
        SettingsService settings,
        MessageQueue messageQueue,
        IndividualVoiceService individualVoiceService)
    {
        _settings = settings;
        _messageQueue = messageQueue;
        _individualVoiceService = individualVoiceService;
    }

    public async Task<TtsProcessResult> ProcessIncomingMessage(
        string username,
        string voiceName,
        string message,
        string platform)
    {
        // 1. Ранняя проверка на пустое сообщение
        if (string.IsNullOrWhiteSpace(message))
        {
            return TtsProcessResult.Empty();
        }

        // 2. Проверка черного списка
        if (!string.IsNullOrWhiteSpace(username) &&
            _settings.Settings.ListBoxItemSourceBlackList.Contains(username.Trim()))
        {
            return TtsProcessResult.Blacklisted();
        }

        // 3. Команды пропуска сообщений (skip / skip all)
        if (_settings.Settings.CheckBoxAllowSkipMessages)
        {
            string trimmedMessage = message.Trim();
            if (!string.IsNullOrEmpty(_settings.Settings.SkipMessage) &&
                string.Equals(trimmedMessage, _settings.Settings.SkipMessage, StringComparison.OrdinalIgnoreCase))
            {
                _messageQueue.SkipCurrent();
                return TtsProcessResult.Skipped();
            }

            if (!string.IsNullOrEmpty(_settings.Settings.SkipAllMessages) &&
                string.Equals(trimmedMessage, _settings.Settings.SkipAllMessages, StringComparison.OrdinalIgnoreCase))
            {
                _messageQueue.Clear();
                _messageQueue.SkipCurrent();
                return TtsProcessResult.Skipped();
            }
        }

        // 4. Проверка команд и префиксов игнорирования
        // 4. Проверка команд и префиксов игнорирования
        bool isCommandMessage = false;

        string? nttsCmd = _settings.Settings.NttsVoiceChangeCommand?.Trim();
        string trimmedStart = message.TrimStart();

        // Проверяем, задана ли команда и начинается ли сообщение с неё (только если NTTS включён)
        if (_settings.Settings.CheckBoxNttsVoicesIsEnabled &&
            !string.IsNullOrWhiteSpace(nttsCmd) &&
            trimmedStart.StartsWith(nttsCmd, StringComparison.OrdinalIgnoreCase) &&
            (trimmedStart.Length == nttsCmd.Length || char.IsWhiteSpace(trimmedStart[nttsCmd.Length])))
        {
            // Не прерываем выполнение — команда отработает дальше (смена голоса),
            // но помечаем как не требующую синтеза речи самого текста команды.
            isCommandMessage = true;
        }
        else if (!string.IsNullOrEmpty(_settings.Settings.DoNotTtsIfStartWith) &&
                 message.StartsWith(_settings.Settings.DoNotTtsIfStartWith, StringComparison.OrdinalIgnoreCase))
        {
            return TtsProcessResult.Filtered();
        }

        // ==============================
        // ВЫДАЧА ГОЛОСА
        // ==============================

        string? nttsSpeaker = null;
        IndividualVoiceSettings? individualVoice = null;

        if (_settings.Settings.CheckBoxNttsVoicesIsEnabled)
        {
            nttsSpeaker = await _individualVoiceService.GetNttsVoiceForUserAsync(
                username,
                platform,
                message);
        }
        else if (_settings.Settings.IndividualVoicesEnabled)
        {
            individualVoice = _individualVoiceService.GetVoiceForUser(
                username,
                platform);
        }

        // Если это была служебная команда, смена голоса уже отработала выше.
        // Не тратим ресурсы на обработку текста и не озвучиваем.
        if (isCommandMessage)
        {
            return TtsProcessResult.Filtered();
        }

        // ==============================
        // ОБРАБОТКА ТЕКСТА
        // ==============================

        string processedMessage = message;

        // Многоточия / несколько точек
        processedMessage = MultiDotRegex.Replace(processedMessage, " . ");

        // Ссылки → слово "ссылка"
        processedMessage = LinkRegex.Replace(processedMessage, "ссылка");

        // Эмодзи
        if (_settings.Settings.RemoveEmoji)
        {
            processedMessage = EmojiRegex.Replace(processedMessage, string.Empty);
        }

        // ==============================
        // ЗАМЕНА СЛОВ И ФРАЗ
        // ==============================

        if (_settings.Settings.WordReplacements is { Count: > 0 })
        {
            // 1. Сначала заменяем фразы, содержащие пробелы
            foreach (var kvp in _settings.Settings.WordReplacements)
            {
                if (kvp.Key.Contains(' ') && !string.IsNullOrWhiteSpace(kvp.Key))
                {
                    processedMessage = Regex.Replace(
                        processedMessage,
                        Regex.Escape(kvp.Key),
                        kvp.Value,
                        RegexOptions.IgnoreCase);
                }
            }

            // 2. Отдельные слова с сохранением знаков препинания
            var processed = new StringBuilder(processedMessage.Length);
            string[] words = WhitespaceRegex.Split(processedMessage);

            foreach (string word in words)
            {
                if (string.IsNullOrEmpty(word))
                    continue;

                var (leading, core, trailing) = SplitPunctuation(word);

                if (!string.IsNullOrEmpty(core) && TryGetWordReplacement(core, out string? coreReplacement))
                {
                    processed.Append(leading).Append(coreReplacement).Append(trailing);
                }
                else if (TryGetWordReplacement(word, out string? directReplacement))
                {
                    processed.Append(directReplacement);
                }
                else
                {
                    processed.Append(word);
                }

                processed.Append(' ');
            }

            processedMessage = processed.ToString().Trim();
        }

        // ==============================
        // ЗАМЕНА СЛОВ ПРИ СОВПАДЕНИИ ЧАСТИ СИМВОЛОВ (CharWordReplacements)
        // ==============================

        if (_settings.Settings.CharWordReplacements is { Count: > 0 })
        {
            var processed = new StringBuilder(processedMessage.Length);
            string[] words = WhitespaceRegex.Split(processedMessage);

            foreach (string word in words)
            {
                if (string.IsNullOrEmpty(word))
                    continue;

                var (leading, core, trailing) = SplitPunctuation(word);
                string target = !string.IsNullOrEmpty(core) ? core : word;

                if (TryGetCharWordReplacement(target, out string? replacement))
                {
                    processed.Append(leading).Append(replacement).Append(trailing);
                }
                else if (TryGetCharWordReplacement(word, out string? directReplacement))
                {
                    processed.Append(directReplacement);
                }
                else
                {
                    processed.Append(word);
                }

                processed.Append(' ');
            }

            processedMessage = processed.ToString().Trim();
        }

        // ==============================
        // ФИЛЬТР МАТА (BadWordFilter)
        // ==============================

        if (_settings.Settings.BadWordFilter)
        {
            processedMessage = FilterBadWords(processedMessage);
        }

        // ==============================
        // ТРАНСЛИТЕРАЦИЯ И ЗАМЕНА СИМВОЛОВ
        // ==============================

        processedMessage = Transliteration.LatinToCyrillic(processedMessage);
        processedMessage = NormalizeTransliteratedChars(processedMessage);

        if (string.IsNullOrWhiteSpace(processedMessage))
            return TtsProcessResult.Empty();

        // ==============================
        // СБОРКА РЕЗУЛЬТАТА (озвучка)
        // ==============================

        if (nttsSpeaker != null)
        {
            return TtsProcessResult.Success(new ProcessedTtsMessage
            {
                Text = processedMessage,
                VoiceName = nttsSpeaker,
                Engine = TtsEngine.Ntts,
                VoiceVolume = 0,
                VoiceSpeed = 0,
                Platform = platform
            });
        }

        if (individualVoice != null)
        {
            return TtsProcessResult.Success(new ProcessedTtsMessage
            {
                Text = processedMessage,
                VoiceName = individualVoice.Name,
                Engine = TtsEngine.Auto,
                VoiceVolume = individualVoice.Volume,
                VoiceSpeed = individualVoice.Speed,
                Platform = platform
            });
        }

        return TtsProcessResult.Success(new ProcessedTtsMessage
        {
            Text = processedMessage,
            VoiceName = _settings.Settings.SelectedSingleVoice,
            VoiceVolume = _settings.Settings.StandardVoiceVolume,
            VoiceSpeed = _settings.Settings.StandardVoiceSpeed,
            Platform = platform
        });
    }

    private static (string leading, string core, string trailing) SplitPunctuation(string word)
    {
        int start = 0;
        while (start < word.Length && char.IsPunctuation(word[start]))
            start++;

        int end = word.Length - 1;
        while (end >= start && char.IsPunctuation(word[end]))
            end--;

        if (start > end)
            return (word, string.Empty, string.Empty);

        return (word[..start], word[start..(end + 1)], word[(end + 1)..]);
    }

    private static string NormalizeTransliteratedChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                'q' => 'к',
                'Q' => 'К',
                'w' => 'в',
                'W' => 'В',
                'y' => 'и',
                'Y' => 'И',
                'ѵ' => 'й',
                'Ѵ' => 'Й',
                _ => c
            });
        }
        return sb.ToString();
    }

    private bool TryGetWordReplacement(string word, out string? replacement)
    {
        if (_settings.Settings.WordReplacements == null || _settings.Settings.WordReplacements.Count == 0)
        {
            replacement = null;
            return false;
        }

        return _settings.Settings.WordReplacements.TryGetValue(word, out replacement);
    }

    private bool TryGetCharWordReplacement(string word, out string? replacement)
    {
        replacement = null;
        if (_settings.Settings.CharWordReplacements == null || _settings.Settings.CharWordReplacements.Count == 0)
            return false;

        foreach (var (key, value) in _settings.Settings.CharWordReplacements)
        {
            if (!string.IsNullOrEmpty(key) && word.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                replacement = value;
                return true;
            }
        }

        return false;
    }

    private static bool _badWordFilterInitialized;
    private static readonly object _badWordFilterLock = new();

    private static void EnsureBadWordFilterInitialized()
    {
        if (_badWordFilterInitialized) return;
        lock (_badWordFilterLock)
        {
            if (_badWordFilterInitialized) return;

            try
            {
                var filter = BogaNet.BWF.Filter.BadWordFilter.Instance;
                if (!filter.IsLoaded)
                {
                    string baseDir = AppContext.BaseDirectory;
                    var filesToLoad = new List<Tuple<string, string>>();

                    foreach (var tuple in BogaNet.BWF.BWFConstants.BWF_LTR)
                    {
                        string lang = tuple.Item1;
                        string relPath = tuple.Item2;
                        string cleanRel = relPath.TrimStart('.', '/', '\\');
                        string fullPath = Path.Combine(baseDir, cleanRel);

                        if (File.Exists(fullPath))
                        {
                            filesToLoad.Add(Tuple.Create(lang, fullPath));
                        }
                    }

                    if (filesToLoad.Count > 0)
                    {
                        filter.LoadFiles(true, filesToLoad.ToArray());
                    }
                    else
                    {
                        filter.LoadFiles(true, BogaNet.BWF.BWFConstants.BWF_LTR);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка инициализации фильтра мата BadWordFilter");
            }
            finally
            {
                _badWordFilterInitialized = true;
            }
        }
    }

    private static readonly System.Reflection.FieldInfo? BadwordsRegexField =
        typeof(BogaNet.BWF.Filter.BadWordFilter).GetField("_exactBadwordsRegex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static string ReplaceBadWords(BogaNet.BWF.Filter.BadWordFilter filter, string text, string replacement)
    {
        if (BadwordsRegexField?.GetValue(filter) is Dictionary<string, Regex> regexDict && regexDict.Count > 0)
        {
            string result = text;
            foreach (var regex in regexDict.Values)
            {
                result = regex.Replace(result, replacement);
            }
            return result;
        }

        return filter.ReplaceAll(text);
    }

    private string FilterBadWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            EnsureBadWordFilterInitialized();

            var filter = BogaNet.BWF.Filter.BadWordFilter.Instance;
            if (!filter.IsLoaded && filter.Count == 0)
                return text;

            var placeholders = new Dictionary<string, string>();
            string textToFilter = text;

            if (_settings.Settings.BadWordFilterWhitelist is { Count: > 0 } whitelist)
            {
                int tokenIndex = 0;
                foreach (var exemptWord in whitelist)
                {
                    if (string.IsNullOrWhiteSpace(exemptWord))
                        continue;

                    string pattern = @"(?<![\p{L}\p{Nd}_])" + Regex.Escape(exemptWord.Trim()) + @"(?![\p{L}\p{Nd}_])";
                    textToFilter = Regex.Replace(textToFilter, pattern, match =>
                    {
                        string placeholder = $"__BWF_SAFE_{tokenIndex++}__";
                        placeholders[placeholder] = match.Value;
                        return placeholder;
                    }, RegexOptions.IgnoreCase);
                }
            }

            string replacement = _settings.Settings.BadWordReplacement ?? "***";
            string filtered = ReplaceBadWords(filter, textToFilter, replacement);

            if (placeholders.Count > 0)
            {
                foreach (var kvp in placeholders)
                {
                    filtered = filtered.Replace(kvp.Key, kvp.Value);
                }
            }

            return filtered;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при фильтрации мата");
            return text;
        }
    }
}