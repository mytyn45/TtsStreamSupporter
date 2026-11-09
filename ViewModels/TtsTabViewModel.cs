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
    // ФИЛЬТР МАТА
    // =========================================================

    [ObservableProperty]
    private bool _badWordsFilter;

    [ObservableProperty]
    private string? _badWordReplacement = "***";

    [ObservableProperty]
    private string? _textBoxAddToBadWordFilterWhitelist;

    [ObservableProperty]
    private string? _selectedBadWordFilterWhitelistItem;

    public ObservableCollection<string> ListBoxItemSourceBadWordFilterWhitelist { get; } = new();

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
        BadWordsFilter = _settings.Settings.BadWordFilter;
        BadWordReplacement = _settings.Settings.BadWordReplacement;

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

        // Белый список фильтра мата
        if (_settings.Settings.BadWordFilterWhitelist != null)
        {
            foreach (var word in _settings.Settings.BadWordFilterWhitelist)
                ListBoxItemSourceBadWordFilterWhitelist.Add(word);
        }

        // Замена слов
        foreach (var kvp in _settings.Settings.WordReplacements)
            _allWordReplacements.Add(new WordReplacementItemViewModel(kvp.Key, kvp.Value));
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

    partial void OnBadWordsFilterChanged(bool value)
    {
        _settings.Settings.BadWordFilter = value;
        _ = _settings.DelayedSave();
    }

    partial void OnBadWordReplacementChanged(string? value)
    {
        _settings.Settings.BadWordReplacement = value;
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

    partial void OnSelectedWordReplacementItemChanged(WordReplacementItemViewModel? value)
    {
        if (value != null)
        {
            TextBoxWordToReplace = value.Word;
            TextBoxReplacementWord = value.Replacement;
        }
    }

    partial void OnTextBoxCharToFindChanged(string? value)
    {
        ApplyCharFilter();
    }

    partial void OnTextBoxCharReplacementWordChanged(string? value)
    {
        ApplyCharFilter();
    }

    partial void OnSelectedCharReplacementItemChanged(WordReplacementItemViewModel? value)
    {
        if (value != null)
        {
            TextBoxCharToFind = value.Word;
            TextBoxCharReplacementWord = value.Replacement;
        }
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
    // BAD WORDS WHITELIST
    // =========================================================

    [RelayCommand]
    private void AddToBadWordFilterWhitelist()
    {
        string? word = TextBoxAddToBadWordFilterWhitelist?.Trim();
        if (string.IsNullOrWhiteSpace(word))
            return;

        if (!ListBoxItemSourceBadWordFilterWhitelist.Any(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase)))
        {
            ListBoxItemSourceBadWordFilterWhitelist.Insert(0, word);
            _settings.Settings.BadWordFilterWhitelist = ListBoxItemSourceBadWordFilterWhitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _ = _settings.DelayedSave();
        }

        TextBoxAddToBadWordFilterWhitelist = string.Empty;
    }

    [RelayCommand]
    private void RemoveFromBadWordFilterWhitelist()
    {
        if (SelectedBadWordFilterWhitelistItem == null)
            return;

        ListBoxItemSourceBadWordFilterWhitelist.Remove(SelectedBadWordFilterWhitelistItem);
        _settings.Settings.BadWordFilterWhitelist = ListBoxItemSourceBadWordFilterWhitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
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

        var existingKey = _settings.Settings.WordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, word, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.WordReplacements.Remove(existingKey);
        }

        _settings.Settings.WordReplacements[word] = replacement;
        _ = _settings.DelayedSave();

        var existingItem = _allWordReplacements
            .FirstOrDefault(i => string.Equals(i.Word, word, StringComparison.OrdinalIgnoreCase));

        if (existingItem != null)
        {
            existingItem.Word = word;
            existingItem.Replacement = replacement;
        }
        else
        {
            _allWordReplacements.Insert(0, new WordReplacementItemViewModel(word, replacement));
        }

        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
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
            _ = _settings.DelayedSave();
        }

        _allWordReplacements.Remove(itemToRemove);
        SelectedWordReplacementItem = null;

        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string queryWord = TextBoxWordToReplace?.Trim() ?? string.Empty;
        string queryRep = TextBoxReplacementWord?.Trim() ?? string.Empty;

        bool hasWordQuery = !string.IsNullOrEmpty(queryWord);
        bool hasRepQuery = !string.IsNullOrEmpty(queryRep);

        FilteredWordReplacements.Clear();
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

                FilteredWordReplacements.Add(item);
            }
            else
            {
                item.IsWordExactMatch = false;
                item.IsReplacementExactMatch = false;
            }
        }

        IsExactMatchFound = anyExact;
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
    public IRelayCommand ButtonAddToBadWordFilterWhitelist => AddToBadWordFilterWhitelistCommand;
    public IRelayCommand ButtonRemoveFromBadWordFilterWhitelist => RemoveFromBadWordFilterWhitelistCommand;
    public IRelayCommand ButtonAddWordReplacement => AddWordReplacementCommand;
    public IRelayCommand ButtonRemoveWordReplacement => RemoveWordReplacementCommand;
    public IRelayCommand ButtonAddCharReplacement => AddCharReplacementCommand;
    public IRelayCommand ButtonRemoveCharReplacement => RemoveCharReplacementCommand;
}
