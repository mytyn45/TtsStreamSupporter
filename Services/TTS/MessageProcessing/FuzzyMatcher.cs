using System;
using System.Collections.Generic;

namespace TtsStreamSupporter.Services;

/// <summary>
/// Помощник для мягкого (нечеткого) сравнения слов с расчетом расстояния Дамерау-Левенштейна
/// и защитой от конфликтов между правилами.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Максимальное число ошибок, допустимое для конкретного слова с учетом его длины.
    /// Предотвращает ложные срабатывания на коротких словах (1-4 символа).
    /// </summary>
    public static int GetMaxAllowedErrors(string word, int configuredMaxErrors)
    {
        if (configuredMaxErrors <= 0 || string.IsNullOrWhiteSpace(word))
            return 0;

        int len = word.Trim().Length;

        // Слова до 2 символов (предлоги, союзы): только строгое совпадение
        if (len <= 2)
            return 0;

        // Короткие слова 3-4 символа: максимум 1 опечатка
        if (len <= 4)
            return Math.Min(configuredMaxErrors, 1);

        // Слова 5+ символов: допускается настроенное число ошибок
        return configuredMaxErrors;
    }

    /// <summary>
    /// Расчет расстояния Дамерау-Левенштейна (вставки, удаления, замены и перестановки соседних символов).
    /// Сравнение регистронезависимое.
    /// </summary>
    public static int DamerauLevenshteinDistance(string? s1, string? s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        int len1 = s1.Length;
        int len2 = s2.Length;

        int[,] d = new int[len1 + 1, len2 + 1];

        for (int i = 0; i <= len1; i++) d[i, 0] = i;
        for (int j = 0; j <= len2; j++) d[0, j] = j;

        for (int i = 1; i <= len1; i++)
        {
            char c1 = char.ToLowerInvariant(s1[i - 1]);

            for (int j = 1; j <= len2; j++)
            {
                char c2 = char.ToLowerInvariant(s2[j - 1]);
                int cost = (c1 == c2) ? 0 : 1;

                int min = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                // Перестановка двух соседних букв
                if (i > 1 && j > 1 &&
                    char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 2]) &&
                    char.ToLowerInvariant(s1[i - 2]) == char.ToLowerInvariant(s2[j - 1]))
                {
                    min = Math.Min(min, d[i - 2, j - 2] + cost);
                }

                d[i, j] = min;
            }
        }

        return d[len1, len2];
    }

    /// <summary>
    /// Проверяет, конфликтуют ли два слова при текущем пороге ошибок.
    /// Конфликт возникает, если слова не одинаковы, но расстояние между ними не превышает
    /// допустимый порог ошибок для любого из них.
    /// </summary>
    public static bool AreInConflict(string word1, string word2, int configuredMaxErrors) =>
        AreInConflict(word1, word2, configuredMaxErrors, out _);

    public static bool AreInConflict(string word1, string word2, int configuredMaxErrors, out int distance)
    {
        distance = 0;
        if (string.IsNullOrWhiteSpace(word1) || string.IsNullOrWhiteSpace(word2))
            return false;

        string w1 = word1.Trim();
        string w2 = word2.Trim();

        // Точное совпадение не считается нечетким конфликтом (это дубликат)
        if (string.Equals(w1, w2, StringComparison.OrdinalIgnoreCase))
        {
            distance = 0;
            return false;
        }

        if (configuredMaxErrors <= 0)
            return false;

        // Фразы с пробелами проверяются отдельно
        if (w1.Contains(' ') || w2.Contains(' '))
            return false;

        int allowed1 = GetMaxAllowedErrors(w1, configuredMaxErrors);
        int allowed2 = GetMaxAllowedErrors(w2, configuredMaxErrors);
        int maxAllowed = Math.Min(allowed1, allowed2);

        if (maxAllowed <= 0)
            return false;

        if (Math.Abs(w1.Length - w2.Length) > maxAllowed)
            return false;

        distance = DamerauLevenshteinDistance(w1, w2);
        return distance <= maxAllowed;
    }

    /// <summary>
    /// Ищет наилучшую замену для слова:
    /// 1. Сначала точное совпадение.
    /// 2. Если не найдено и configuredMaxErrors > 0, ищет ближайшее слово.
    /// 3. При обнаружении конфликта (два разных правила с одинаковым минимальным расстоянием) возвращает false.
    /// </summary>
    public static bool FindBestMatch(
        string inputWord,
        IDictionary<string, string>? replacements,
        int configuredMaxErrors,
        out string? bestReplacement)
    {
        bestReplacement = null;

        if (string.IsNullOrWhiteSpace(inputWord) || replacements == null || replacements.Count == 0)
            return false;

        string trimmed = inputWord.Trim();

        // 1. Точное совпадение (всегда приоритет)
        if (replacements.TryGetValue(trimmed, out bestReplacement))
            return true;

        if (configuredMaxErrors <= 0 || trimmed.Length <= 2)
            return false;

        // 2. Нечеткий поиск
        int minDistance = int.MaxValue;
        string? candidateReplacement = null;
        bool hasConflict = false;

        foreach (var (key, value) in replacements)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains(' '))
                continue;

            int maxAllowed = GetMaxAllowedErrors(key, configuredMaxErrors);
            if (maxAllowed <= 0)
                continue;

            if (Math.Abs(trimmed.Length - key.Length) > maxAllowed)
                continue;

            int dist = DamerauLevenshteinDistance(trimmed, key);
            if (dist <= maxAllowed)
            {
                if (dist < minDistance)
                {
                    minDistance = dist;
                    candidateReplacement = value;
                    hasConflict = false;
                }
                else if (dist == minDistance)
                {
                    // Два разных правила на одинаковом расстоянии
                    if (!string.Equals(candidateReplacement, value, StringComparison.OrdinalIgnoreCase))
                    {
                        hasConflict = true;
                    }
                }
            }
        }

        if (!hasConflict && candidateReplacement != null)
        {
            bestReplacement = candidateReplacement;
            return true;
        }

        bestReplacement = null;
        return false;
    }
}
