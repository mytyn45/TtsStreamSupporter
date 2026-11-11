using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public enum WordSortColumn
{
    None,
    Word,
    Replacement,
    Date
}

public partial class TtsTabViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly IVoiceDiscoveryService _voiceDiscoveryService;

    // Списки всех замен (полная база для фильтрации)
    private readonly List<WordReplacementItemViewModel> _allWordReplacements = new();
    private readonly List<WordReplacementItemViewModel> _allCharWordReplacements = new();

    // =========================================================
    // ГОЛОСА
    // =========================================================

    public ObservableCollection<string> ComboBoxItemSourceInstalledVoices { get; } = new();

    [ObservableProperty]
    private string? _comboBoxSelectedItemInstalledVoices;

    [ObservableProperty]
    private int _sliderSingleVoiceVolume;

    [ObservableProperty]
    private int _sliderSingleVoiceSpeed;

    // =========================================================
    // PIPER ГОЛОСА
    // =========================================================

    public ObservableCollection<string> ComboBoxItemSourceAvailablePiperVoices { get; } = new();

    [ObservableProperty]
    private string? _selectedPiperVoiceToDownload;

    [ObservableProperty]
    private bool _isPiperDownloading;

    [ObservableProperty]
    private bool _isPiperDownloadProgressVisible;

    [ObservableProperty]
    private double _piperDownloadProgressValue;

    [ObservableProperty]
    private bool _isPiperDownloadIndeterminate;

    [ObservableProperty]
    private string _piperDownloadStatusText = string.Empty;

    [ObservableProperty]
    private string _piperDownloadProgressText = string.Empty;

    [ObservableProperty]
    private IBrush _piperDownloadStatusBrush = Brushes.Gray;

    // =========================================================
    // ЗВУКОВЫЕ ЭФФЕКТЫ
    // =========================================================

    [ObservableProperty]
    private bool _checkBoxNormalizationTts;

    [ObservableProperty]
    private int _sliderCompressorPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterVolumeMultiplierText))]
    private double _sliderMasterVolumeMultiplier = 1.0;

    public string MasterVolumeMultiplierText => $"x{SliderMasterVolumeMultiplier:F1}";

    // =========================================================
    // КОМАНДЫ ЧАТА
    // =========================================================

    [ObservableProperty]
    private string _doNotTtsIfStartWith = "!";

    [ObservableProperty]
    private string _skipMessage = "!c";

    [ObservableProperty]
    private string _skipAllMessages = "!cв";

    [ObservableProperty]
    private bool _checkBoxAllowSkipMessages;



    // =========================================================
    // ЧЁРНЫЙ СПИСОК
    // =========================================================

    [ObservableProperty]
    private string? _textBoxAddToBlackList;

    [ObservableProperty]
    private string? _selectedBlacklistItem;

    public ObservableCollection<string> ListBoxItemSourceBlacklist { get; } = new();

    // =========================================================
    // ЗАМЕНА СЛОВ
    // =========================================================

    [ObservableProperty]
    private string? _textBoxWordToReplace;

    [ObservableProperty]
    private string? _textBoxReplacementWord;

    [ObservableProperty]
    private WordReplacementItemViewModel? _selectedWordReplacementItem;

    [ObservableProperty]
    private bool _isExactMatchFound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordFuzzyThresholdText))]
    private int _sliderWordFuzzyThreshold;

    public string WordFuzzyThresholdText => SliderWordFuzzyThreshold switch
    {
        0 => "0 (выкл)",
        1 => "1 ошибка",
        2 => "2 ошибки",
        _ => $"{SliderWordFuzzyThreshold} ошибки"
    };

    [ObservableProperty]
    private bool _isConflictFound;

    [ObservableProperty]
    private string? _conflictWarningText;

    private static readonly IBrush SortActiveBackground = Brush.Parse("#E3F2FD");
    private static readonly IBrush SortActiveBorderBrush = Brush.Parse("#1976D2");
    private static readonly IBrush SortActiveForeground = Brush.Parse("#1565C0");

    private static readonly IBrush SortInactiveBackground = Brushes.Transparent;
    private static readonly IBrush SortInactiveBorderBrush = Brush.Parse("#E0E0E0");
    private static readonly IBrush SortInactiveForeground = Brush.Parse("#555555");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordSortIndicator))]
    [NotifyPropertyChangedFor(nameof(ReplacementSortIndicator))]
    [NotifyPropertyChangedFor(nameof(DateSortIndicator))]
    [NotifyPropertyChangedFor(nameof(WordHeaderBackground))]
    [NotifyPropertyChangedFor(nameof(ReplacementHeaderBackground))]
    [NotifyPropertyChangedFor(nameof(DateHeaderBackground))]
    [NotifyPropertyChangedFor(nameof(WordHeaderBorderBrush))]
    [NotifyPropertyChangedFor(nameof(ReplacementHeaderBorderBrush))]
    [NotifyPropertyChangedFor(nameof(DateHeaderBorderBrush))]
    [NotifyPropertyChangedFor(nameof(WordHeaderForeground))]
    [NotifyPropertyChangedFor(nameof(ReplacementHeaderForeground))]
    [NotifyPropertyChangedFor(nameof(DateHeaderForeground))]
    private WordSortColumn _currentSortColumn = WordSortColumn.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordSortIndicator))]
    [NotifyPropertyChangedFor(nameof(ReplacementSortIndicator))]
    [NotifyPropertyChangedFor(nameof(DateSortIndicator))]
    private bool _isSortAscending = true;

    public string WordSortIndicator => CurrentSortColumn == WordSortColumn.Word
        ? (IsSortAscending ? " ▲" : " ▼")
        : string.Empty;

    public string ReplacementSortIndicator => CurrentSortColumn == WordSortColumn.Replacement
        ? (IsSortAscending ? " ▲" : " ▼")
        : string.Empty;

    public string DateSortIndicator => CurrentSortColumn == WordSortColumn.Date
        ? (IsSortAscending ? " ▲" : " ▼")
        : string.Empty;

    public IBrush WordHeaderBackground => CurrentSortColumn == WordSortColumn.Word ? SortActiveBackground : SortInactiveBackground;
    public IBrush WordHeaderBorderBrush => CurrentSortColumn == WordSortColumn.Word ? SortActiveBorderBrush : SortInactiveBorderBrush;
    public IBrush WordHeaderForeground => CurrentSortColumn == WordSortColumn.Word ? SortActiveForeground : SortInactiveForeground;

    public IBrush ReplacementHeaderBackground => CurrentSortColumn == WordSortColumn.Replacement ? SortActiveBackground : SortInactiveBackground;
    public IBrush ReplacementHeaderBorderBrush => CurrentSortColumn == WordSortColumn.Replacement ? SortActiveBorderBrush : SortInactiveBorderBrush;
    public IBrush ReplacementHeaderForeground => CurrentSortColumn == WordSortColumn.Replacement ? SortActiveForeground : SortInactiveForeground;

    public IBrush DateHeaderBackground => CurrentSortColumn == WordSortColumn.Date ? SortActiveBackground : SortInactiveBackground;
    public IBrush DateHeaderBorderBrush => CurrentSortColumn == WordSortColumn.Date ? SortActiveBorderBrush : SortInactiveBorderBrush;
    public IBrush DateHeaderForeground => CurrentSortColumn == WordSortColumn.Date ? SortActiveForeground : SortInactiveForeground;

    public ObservableCollection<WordReplacementItemViewModel> FilteredWordReplacements { get; } = new();

    // =========================================================
    // ЗАМЕНА СИМВОЛОВ
    // =========================================================

    [ObservableProperty]
    private string? _textBoxCharToFind;

    [ObservableProperty]
    private string? _textBoxCharReplacementWord;

    [ObservableProperty]
    private WordReplacementItemViewModel? _selectedCharReplacementItem;

    [ObservableProperty]
    private bool _isCharExactMatchFound;

    public ObservableCollection<WordReplacementItemViewModel> FilteredCharWordReplacements { get; } = new();

    // =========================================================
    // КОНСТРУКТОР
    // =========================================================

    public TtsTabViewModel(
        SettingsService settings,
        IVoiceDiscoveryService? voiceDiscoveryService = null)
    {
        _settings = settings;
        _voiceDiscoveryService = voiceDiscoveryService ?? new VoiceDiscoveryService();

        // Загрузка доступных системных и скачанных голосов
        LoadVoicesList();

        if (Design.IsDesignMode)
            return;

        // Загрузка настроек
        SliderSingleVoiceVolume = _settings.Settings.StandardVoiceVolume;
        SliderSingleVoiceSpeed = _settings.Settings.StandardVoiceSpeed;
        SliderCompressorPower = _settings.Settings.CompressorPower;
        SliderMasterVolumeMultiplier = _settings.Settings.MasterVolumeMultiplier <= 0f
            ? 1.0
            : Math.Round(_settings.Settings.MasterVolumeMultiplier, 1);

        ComboBoxSelectedItemInstalledVoices = _settings.Settings.SelectedSingleVoice;
        CheckBoxNormalizationTts = _settings.Settings.CheckBoxNormalizationTts;
        CheckBoxAllowSkipMessages = _settings.Settings.CheckBoxAllowSkipMessages;

        if (!string.IsNullOrEmpty(_settings.Settings.DoNotTtsIfStartWith))
            _doNotTtsIfStartWith = _settings.Settings.DoNotTtsIfStartWith;
        if (!string.IsNullOrEmpty(_settings.Settings.SkipMessage))
            _skipMessage = _settings.Settings.SkipMessage;
        if (!string.IsNullOrEmpty(_settings.Settings.SkipAllMessages))
            _skipAllMessages = _settings.Settings.SkipAllMessages;

        // Piper доступные для загрузки голоса
        LoadAvailablePiperVoices();

        // Черный список
        foreach (var user in _settings.Settings.ListBoxItemSourceBlackList)
            ListBoxItemSourceBlacklist.Add(user);

        // Замена слов
        SliderWordFuzzyThreshold = _settings.Settings.WordReplacementMaxErrors;
        foreach (var kvp in _settings.Settings.WordReplacements)
        {
            _settings.Settings.WordReplacementDates.TryGetValue(kvp.Key, out var createdAt);
            _allWordReplacements.Add(new WordReplacementItemViewModel(kvp.Key, kvp.Value, createdAt));
        }
        ApplyFilter();

        // Замена символов
        if (_settings.Settings.CharWordReplacements != null)
        {
            foreach (var kvp in _settings.Settings.CharWordReplacements)
                _allCharWordReplacements.Add(new WordReplacementItemViewModel(kvp.Key, kvp.Value));
        }
        ApplyCharFilter();

        // Выбор первого доступного голоса, если не сохранен
        if (string.IsNullOrWhiteSpace(_comboBoxSelectedItemInstalledVoices))
        {
            _comboBoxSelectedItemInstalledVoices = ComboBoxItemSourceInstalledVoices.FirstOrDefault();
            _settings.Settings.SelectedSingleVoice = _comboBoxSelectedItemInstalledVoices;
        }
    }

    private void LoadVoicesList()
    {
        ComboBoxItemSourceInstalledVoices.Clear();
        foreach (var voice in _voiceDiscoveryService.GetAvailableVoices())
        {
            ComboBoxItemSourceInstalledVoices.Add(voice);
        }
    }

    private void LoadAvailablePiperVoices()
    {
        string voicesFilePath = PiperVoicesData.VoicesFilePath;
        if (File.Exists(voicesFilePath))
        {
            foreach (var item in File.ReadAllLines(voicesFilePath))
            {
                if (!string.IsNullOrWhiteSpace(item))
                    ComboBoxItemSourceAvailablePiperVoices.Add(item.Trim());
            }
        }
    }

    // =========================================================
    // СВОЙСТВА ИЗМЕНЕНИЙ И СОХРАНЕНИЙ
    // =========================================================

    partial void OnComboBoxSelectedItemInstalledVoicesChanged(string? value)
    {
        _settings.Settings.SelectedSingleVoice = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSliderSingleVoiceVolumeChanged(int value)
    {
        _settings.Settings.StandardVoiceVolume = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSliderSingleVoiceSpeedChanged(int value)
    {
        _settings.Settings.StandardVoiceSpeed = value;
        _ = _settings.DelayedSave();
    }

    partial void OnCheckBoxNormalizationTtsChanged(bool value)
    {
        _settings.Settings.CheckBoxNormalizationTts = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSliderCompressorPowerChanged(int value)
    {
        _settings.Settings.CompressorPower = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSliderMasterVolumeMultiplierChanged(double value)
    {
        var rounded = Math.Round(value, 1);
        _settings.Settings.MasterVolumeMultiplier = (float)rounded;
        _ = _settings.DelayedSave();
    }

    partial void OnDoNotTtsIfStartWithChanged(string value)
    {
        _settings.Settings.DoNotTtsIfStartWith = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSkipMessageChanged(string value)
    {
        _settings.Settings.SkipMessage = value;
        _ = _settings.DelayedSave();
    }

    partial void OnSkipAllMessagesChanged(string value)
    {
        _settings.Settings.SkipAllMessages = value;
        _ = _settings.DelayedSave();
    }

    partial void OnCheckBoxAllowSkipMessagesChanged(bool value)
    {
        _settings.Settings.CheckBoxAllowSkipMessages = value;
        _ = _settings.DelayedSave();
    }


    partial void OnSelectedPiperVoiceToDownloadChanged(string? value)
    {
        if (!IsPiperDownloading && IsPiperDownloadProgressVisible)
        {
            IsPiperDownloadProgressVisible = false;
            PiperDownloadStatusText = string.Empty;
            PiperDownloadProgressText = string.Empty;
            PiperDownloadProgressValue = 0;
        }
    }

    partial void OnTextBoxWordToReplaceChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnTextBoxReplacementWordChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSliderWordFuzzyThresholdChanged(int value)
    {
        _settings.Settings.WordReplacementMaxErrors = value;
        _ = _settings.DelayedSave();
        ApplyFilter();
    }

    partial void OnTextBoxCharToFindChanged(string? value)
    {
        ApplyCharFilter();
    }

    partial void OnTextBoxCharReplacementWordChanged(string? value)
    {
        ApplyCharFilter();
    }

    [RelayCommand]
    public void PopulateWordReplacementInputs(WordReplacementItemViewModel? item)
    {
        if (item != null)
        {
            SelectedWordReplacementItem = item;
            TextBoxWordToReplace = item.Word;
            TextBoxReplacementWord = item.Replacement;
        }
    }

    [RelayCommand]
    public void PopulateCharReplacementInputs(WordReplacementItemViewModel? item)
    {
        if (item != null)
        {
            SelectedCharReplacementItem = item;
            TextBoxCharToFind = item.Word;
            TextBoxCharReplacementWord = item.Replacement;
        }
    }

    [RelayCommand]
    private void ClearWordReplacementInputs()
    {
        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
        SelectedWordReplacementItem = null;
        IsConflictFound = false;
        ConflictWarningText = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearCharReplacementInputs()
    {
        TextBoxCharToFind = string.Empty;
        TextBoxCharReplacementWord = string.Empty;
        SelectedCharReplacementItem = null;
        ApplyCharFilter();
    }

    // =========================================================
    // PIPER DOWNLOAD
    // =========================================================

    [RelayCommand]
    public async Task DownloadPiperVoiceAsync()
    {
        var piperVoiceName = SelectedPiperVoiceToDownload;
        if (string.IsNullOrWhiteSpace(piperVoiceName) || IsPiperDownloading)
            return;

        IsPiperDownloading = true;
        IsPiperDownloadProgressVisible = true;
        IsPiperDownloadIndeterminate = true;
        PiperDownloadProgressValue = 0;
        PiperDownloadProgressText = "";
        PiperDownloadStatusText = "Подготовка к загрузке...";
        PiperDownloadStatusBrush = Brushes.Gray;

        var progress = new Progress<PiperDownloadProgress>(p =>
        {
            IsPiperDownloadIndeterminate = false;
            PiperDownloadProgressValue = p.ProgressPercentage;
            PiperDownloadProgressText = $"{p.ProgressPercentage:F0}%";
            if (p.TotalBytes > 0)
            {
                double currentMb = p.DownloadedBytes / (1024.0 * 1024.0);
                double totalMb = p.TotalBytes / (1024.0 * 1024.0);
                PiperDownloadStatusText = $"Загрузка: {currentMb:F1} / {totalMb:F1} МБ";
            }
            else
            {
                double currentMb = p.DownloadedBytes / (1024.0 * 1024.0);
                PiperDownloadStatusText = $"Загрузка: {currentMb:F1} МБ";
            }
        });

        try
        {
            var result = await PiperSharpTTS.DownloadPiperWithProgressAsync(piperVoiceName, progress);
            if (result.Status == PiperDownloadStatus.Success)
            {
                PiperDownloadProgressValue = 100;
                PiperDownloadProgressText = "100%";
                PiperDownloadStatusText = "Голос успешно скачан!";
                PiperDownloadStatusBrush = new SolidColorBrush(Color.Parse("#4CAF50"));

                if (!ComboBoxItemSourceInstalledVoices.Contains(piperVoiceName))
                {
                    ComboBoxItemSourceInstalledVoices.Add(piperVoiceName);
                }
            }
            else if (result.Status == PiperDownloadStatus.AlreadyExists)
            {
                PiperDownloadProgressValue = 100;
                PiperDownloadProgressText = "";
                PiperDownloadStatusText = "Голос уже установлен";
                PiperDownloadStatusBrush = new SolidColorBrush(Color.Parse("#42A5F5"));

                if (!ComboBoxItemSourceInstalledVoices.Contains(piperVoiceName))
                {
                    ComboBoxItemSourceInstalledVoices.Add(piperVoiceName);
                }
            }
            else
            {
                PiperDownloadProgressValue = 0;
                PiperDownloadProgressText = "";
                PiperDownloadStatusText = string.IsNullOrEmpty(result.ErrorMessage)
                    ? "Ошибка при загрузке"
                    : $"Ошибка: {result.ErrorMessage}";
                PiperDownloadStatusBrush = new SolidColorBrush(Color.Parse("#EF5350"));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при загрузке голоса Piper: {VoiceName}", piperVoiceName);
            PiperDownloadProgressValue = 0;
            PiperDownloadProgressText = "";
            PiperDownloadStatusText = "Ошибка при загрузке";
            PiperDownloadStatusBrush = new SolidColorBrush(Color.Parse("#EF5350"));
        }
        finally
        {
            IsPiperDownloading = false;
            IsPiperDownloadIndeterminate = false;
        }
    }

    // =========================================================
    // BLACKLIST
    // =========================================================

    [RelayCommand]
    private void AddToBlackList()
    {
        string? username = TextBoxAddToBlackList?.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return;

        if (!ListBoxItemSourceBlacklist.Any(x => string.Equals(x, username, StringComparison.OrdinalIgnoreCase)))
        {
            ListBoxItemSourceBlacklist.Insert(0, username);
            _settings.Settings.ListBoxItemSourceBlackList = ListBoxItemSourceBlacklist.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _ = _settings.DelayedSave();
        }

        TextBoxAddToBlackList = string.Empty;
    }

    [RelayCommand]
    private void RemoveFromBlackList()
    {
        if (SelectedBlacklistItem == null)
            return;

        ListBoxItemSourceBlacklist.Remove(SelectedBlacklistItem);
        _settings.Settings.ListBoxItemSourceBlackList = ListBoxItemSourceBlacklist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = _settings.DelayedSave();
    }


    // =========================================================
    // WORD REPLACEMENTS
    // =========================================================

    [RelayCommand]
    private void AddWordReplacement()
    {
        var word = TextBoxWordToReplace?.Trim();
        var replacement = TextBoxReplacementWord?.Trim();

        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(replacement))
            return;

        // Проверка конфликтов перед добавлением нового правила
        if (SliderWordFuzzyThreshold > 0)
        {
            foreach (var item in _allWordReplacements)
            {
                if (string.Equals(item.Word, word, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(item.Replacement, replacement, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (FuzzyMatcher.AreInConflict(word, item.Word, SliderWordFuzzyThreshold, out int dist))
                {
                    IsConflictFound = true;
                    ConflictWarningText = $"Конфликт: '{word}' слишком похоже на '{item.Word}' (разница: {dist} симв.)";
                    return;
                }
            }
        }

        var existingKey = _settings.Settings.WordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, word, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.WordReplacements.Remove(existingKey);
        }

        var now = DateTime.Now;
        _settings.Settings.WordReplacements[word] = replacement;
        _settings.Settings.WordReplacementDates[word] = now;
        _ = _settings.DelayedSave();

        var existingItem = _allWordReplacements
            .FirstOrDefault(i => string.Equals(i.Word, word, StringComparison.OrdinalIgnoreCase));

        if (existingItem != null)
        {
            existingItem.Word = word;
            existingItem.Replacement = replacement;
            existingItem.CreatedAt = now;
        }
        else
        {
            _allWordReplacements.Insert(0, new WordReplacementItemViewModel(word, replacement, now));
        }

        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
        IsConflictFound = false;
        ConflictWarningText = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void RemoveWordReplacement()
    {
        WordReplacementItemViewModel? itemToRemove = SelectedWordReplacementItem;

        if (itemToRemove == null && !string.IsNullOrWhiteSpace(TextBoxWordToReplace))
        {
            var word = TextBoxWordToReplace.Trim();
            itemToRemove = _allWordReplacements
                .FirstOrDefault(i => string.Equals(i.Word, word, StringComparison.OrdinalIgnoreCase));
        }

        if (itemToRemove == null)
            return;

        var existingKey = _settings.Settings.WordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, itemToRemove.Word, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.WordReplacements.Remove(existingKey);
            _settings.Settings.WordReplacementDates.Remove(existingKey);
            _ = _settings.DelayedSave();
        }

        _allWordReplacements.Remove(itemToRemove);
        SelectedWordReplacementItem = null;

        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
        IsConflictFound = false;
        ConflictWarningText = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleWordSort(string? column)
    {
        var target = column?.ToLowerInvariant() switch
        {
            "word" => WordSortColumn.Word,
            "replacement" => WordSortColumn.Replacement,
            "date" => WordSortColumn.Date,
            _ => WordSortColumn.None
        };

        if (target == WordSortColumn.None)
            return;

        if (CurrentSortColumn == target)
        {
            // Повторный клик: переключаем направление
            IsSortAscending = !IsSortAscending;
        }
        else
        {
            // Первый клик по столбцу: делаем его активным
            CurrentSortColumn = target;
            IsSortAscending = true;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string queryWord = TextBoxWordToReplace?.Trim() ?? string.Empty;
        string queryRep = TextBoxReplacementWord?.Trim() ?? string.Empty;

        bool hasWordQuery = !string.IsNullOrEmpty(queryWord);
        bool hasRepQuery = !string.IsNullOrEmpty(queryRep);

        // 1. Сброс конфликтов у всех правил
        foreach (var item in _allWordReplacements)
        {
            item.IsConflict = false;
            item.ConflictWith = null;
        }

        // 2. Попарный поиск конфликтов между существующими правилами
        if (SliderWordFuzzyThreshold > 0)
        {
            for (int i = 0; i < _allWordReplacements.Count; i++)
            {
                var a = _allWordReplacements[i];
                for (int j = i + 1; j < _allWordReplacements.Count; j++)
                {
                    var b = _allWordReplacements[j];
                    if (string.Equals(a.Replacement, b.Replacement, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (FuzzyMatcher.AreInConflict(a.Word, b.Word, SliderWordFuzzyThreshold))
                    {
                        a.IsConflict = true;
                        a.ConflictWith = b.Word;
                        b.IsConflict = true;
                        b.ConflictWith = a.Word;
                    }
                }
            }
        }

        // 3. Фильтрация и точное совпадение
        var matchedItems = new List<WordReplacementItemViewModel>();
        bool anyExact = false;

        foreach (var item in _allWordReplacements)
        {
            bool wordContains = !hasWordQuery || item.Word.Contains(queryWord, StringComparison.OrdinalIgnoreCase);
            bool repContains = !hasRepQuery || item.Replacement.Contains(queryRep, StringComparison.OrdinalIgnoreCase);

            bool isMatch = (hasWordQuery && hasRepQuery) ? (wordContains && repContains)
                         : hasWordQuery ? wordContains
                         : hasRepQuery ? repContains
                         : true;

            if (isMatch)
            {
                bool isWordExact = hasWordQuery && string.Equals(item.Word, queryWord, StringComparison.OrdinalIgnoreCase);
                bool isRepExact = hasRepQuery && string.Equals(item.Replacement, queryRep, StringComparison.OrdinalIgnoreCase);

                item.IsWordExactMatch = isWordExact;
                item.IsReplacementExactMatch = isRepExact;

                if (isWordExact || isRepExact)
                    anyExact = true;

                matchedItems.Add(item);
            }
            else
            {
                item.IsWordExactMatch = false;
                item.IsReplacementExactMatch = false;
            }
        }

        // Сортировка элементов
        IEnumerable<WordReplacementItemViewModel> sortedItems = CurrentSortColumn switch
        {
            WordSortColumn.Word => IsSortAscending
                ? matchedItems.OrderBy(x => x.Word, StringComparer.CurrentCultureIgnoreCase)
                : matchedItems.OrderByDescending(x => x.Word, StringComparer.CurrentCultureIgnoreCase),

            WordSortColumn.Replacement => IsSortAscending
                ? matchedItems.OrderBy(x => x.Replacement, StringComparer.CurrentCultureIgnoreCase)
                : matchedItems.OrderByDescending(x => x.Replacement, StringComparer.CurrentCultureIgnoreCase),

            WordSortColumn.Date => IsSortAscending
                ? matchedItems.OrderBy(x => x.CreatedAt)
                : matchedItems.OrderByDescending(x => x.CreatedAt),

            _ => matchedItems
        };

        FilteredWordReplacements.Clear();
        foreach (var item in sortedItems)
        {
            FilteredWordReplacements.Add(item);
        }

        IsExactMatchFound = anyExact;

        // 4. Проверка конфликта текущего ввода с существующими правилами
        if (hasWordQuery && SliderWordFuzzyThreshold > 0 && !anyExact)
        {
            string? conflictingWord = null;
            int conflictDistance = 0;

            foreach (var item in _allWordReplacements)
            {
                if (string.Equals(item.Word, queryWord, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(item.Replacement, queryRep, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (FuzzyMatcher.AreInConflict(queryWord, item.Word, SliderWordFuzzyThreshold, out conflictDistance))
                {
                    conflictingWord = item.Word;
                    break;
                }
            }

            if (conflictingWord != null)
            {
                IsConflictFound = true;
                ConflictWarningText = $"Конфликт: '{queryWord}' слишком похоже на '{conflictingWord}' (разница: {conflictDistance} симв.)";
            }
            else
            {
                IsConflictFound = false;
                ConflictWarningText = null;
            }
        }
        else
        {
            IsConflictFound = false;
            ConflictWarningText = null;
        }
    }

    // =========================================================
    // CHAR REPLACEMENTS
    // =========================================================

    [RelayCommand]
    private void AddCharReplacement()
    {
        var symbol = TextBoxCharToFind?.Trim();
        var replacement = TextBoxCharReplacementWord?.Trim();

        if (string.IsNullOrWhiteSpace(symbol))
            return;

        if (string.IsNullOrWhiteSpace(replacement))
            replacement = symbol;

        var existingKey = _settings.Settings.CharWordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, symbol, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
            _settings.Settings.CharWordReplacements.Remove(existingKey);

        _settings.Settings.CharWordReplacements[symbol] = replacement;
        _ = _settings.DelayedSave();

        var existingItem = _allCharWordReplacements
            .FirstOrDefault(i => string.Equals(i.Word, symbol, StringComparison.OrdinalIgnoreCase));

        if (existingItem != null)
        {
            existingItem.Word = symbol;
            existingItem.Replacement = replacement;
        }
        else
        {
            _allCharWordReplacements.Insert(0, new WordReplacementItemViewModel(symbol, replacement));
        }

        TextBoxCharToFind = string.Empty;
        TextBoxCharReplacementWord = string.Empty;
        ApplyCharFilter();
    }

    [RelayCommand]
    private void RemoveCharReplacement()
    {
        WordReplacementItemViewModel? itemToRemove = SelectedCharReplacementItem;

        if (itemToRemove == null && !string.IsNullOrWhiteSpace(TextBoxCharToFind))
        {
            var symbol = TextBoxCharToFind.Trim();
            itemToRemove = _allCharWordReplacements
                .FirstOrDefault(i => string.Equals(i.Word, symbol, StringComparison.OrdinalIgnoreCase));
        }

        if (itemToRemove == null)
            return;

        var existingKey = _settings.Settings.CharWordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, itemToRemove.Word, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.CharWordReplacements.Remove(existingKey);
            _ = _settings.DelayedSave();
        }

        _allCharWordReplacements.Remove(itemToRemove);
        SelectedCharReplacementItem = null;

        TextBoxCharToFind = string.Empty;
        TextBoxCharReplacementWord = string.Empty;
        ApplyCharFilter();
    }


    private void ApplyCharFilter()
    {
        string querySymbol = TextBoxCharToFind?.Trim() ?? string.Empty;
        string queryRep = TextBoxCharReplacementWord?.Trim() ?? string.Empty;

        bool hasSymbolQuery = !string.IsNullOrEmpty(querySymbol);
        bool hasRepQuery = !string.IsNullOrEmpty(queryRep);

        FilteredCharWordReplacements.Clear();
        bool anyExact = false;

        foreach (var item in _allCharWordReplacements)
        {
            bool symbolContains = !hasSymbolQuery || item.Word.Contains(querySymbol, StringComparison.OrdinalIgnoreCase);
            bool repContains = !hasRepQuery || item.Replacement.Contains(queryRep, StringComparison.OrdinalIgnoreCase);

            bool isMatch = (hasSymbolQuery && hasRepQuery) ? (symbolContains && repContains)
                         : hasSymbolQuery ? symbolContains
                         : hasRepQuery ? repContains
                         : true;

            if (isMatch)
            {
                bool isSymbolExact = hasSymbolQuery && string.Equals(item.Word, querySymbol, StringComparison.OrdinalIgnoreCase);
                bool isRepExact = hasRepQuery && string.Equals(item.Replacement, queryRep, StringComparison.OrdinalIgnoreCase);

                item.IsWordExactMatch = isSymbolExact;
                item.IsReplacementExactMatch = isRepExact;

                if (isSymbolExact || isRepExact)
                    anyExact = true;

                FilteredCharWordReplacements.Add(item);
            }
            else
            {
                item.IsWordExactMatch = false;
                item.IsReplacementExactMatch = false;
            }
        }

        IsCharExactMatchFound = anyExact;
    }

    // =========================================================
    // АЛИАСЫ КОМАНД ДЛЯ ПРИВЯЗОК XAML
    // =========================================================

    public IAsyncRelayCommand ButtonDownloadPiperVoice => DownloadPiperVoiceCommand;
    public IRelayCommand ButtonAddToBlackList => AddToBlackListCommand;
    public IRelayCommand ButtonRemoveFromBlackList => RemoveFromBlackListCommand;
    public IRelayCommand ButtonAddWordReplacement => AddWordReplacementCommand;
    public IRelayCommand ButtonRemoveWordReplacement => RemoveWordReplacementCommand;
    public IRelayCommand ButtonClearWordReplacementInputs => ClearWordReplacementInputsCommand;
    public IRelayCommand ButtonAddCharReplacement => AddCharReplacementCommand;
    public IRelayCommand ButtonRemoveCharReplacement => RemoveCharReplacementCommand;
    public IRelayCommand ButtonClearCharReplacementInputs => ClearCharReplacementInputsCommand;
    public IRelayCommand<string?> ButtonToggleWordSort => ToggleWordSortCommand;
}
