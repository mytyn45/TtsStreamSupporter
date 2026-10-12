using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NickBuhro.Translit;
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
}