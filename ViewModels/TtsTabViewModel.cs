using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Speech.Synthesis;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.ViewModels;

public class TtsTabViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    public event PropertyChangedEventHandler? PropertyChanged;
    private string? _comboBoxSelectedItemInstalledVoices;
    private int _sliderSingleVoiceVolume;
    private int _sliderSingleVoiceSpeed;
    private int _sliderCompressorPower;
    private double _sliderMasterVolumeMultiplier = 1.0;
    private bool _checkBoxNormalizationTts;
    private bool _checkBoxAllowSkipMessages;
    private ObservableCollection<string> _availableVoices = [];
    public ObservableCollection<string> ComboBoxSelectedItemDownloadPiperVoice { get; set; } = new ObservableCollection<string>();
    private readonly System.Collections.Generic.List<WordReplacementItemViewModel> _allWordReplacements = new();
    public ObservableCollection<WordReplacementItemViewModel> FilteredWordReplacements { get; } = new();
    private WordReplacementItemViewModel? _selectedWordReplacementItem;
    private bool _isExactMatchFound;
    private bool _isFiltering;

    private readonly System.Collections.Generic.List<WordReplacementItemViewModel> _allCharWordReplacements = new();
    public ObservableCollection<WordReplacementItemViewModel> FilteredCharWordReplacements { get; } = new();
    private WordReplacementItemViewModel? _selectedCharReplacementItem;
    private bool _isCharExactMatchFound;
    private bool _isCharFiltering;

    public WordReplacementItemViewModel? SelectedWordReplacementItem
    {
        get => _selectedWordReplacementItem;
        set
        {
            _selectedWordReplacementItem = value;
            OnPropertyChanged();

            if (value != null)
            {
                _textBoxWordToReplace = value.Word;
                _textBoxReplacementWord = value.Replacement;

                OnPropertyChanged(nameof(TextBoxWordToReplace));
                OnPropertyChanged(nameof(TextBoxReplacementWord));
            }
        }
    }

    public WordReplacementItemViewModel? SelectedCharReplacementItem
    {
        get => _selectedCharReplacementItem;
        set
        {
            _selectedCharReplacementItem = value;
            OnPropertyChanged();

            if (value != null)
            {
                _textBoxCharToFind = value.Word;
                _textBoxCharReplacementWord = value.Replacement;

                OnPropertyChanged(nameof(TextBoxCharToFind));
                OnPropertyChanged(nameof(TextBoxCharReplacementWord));
            }
        }
    }

    public bool IsExactMatchFound
    {
        get => _isExactMatchFound;
        set
        {
            if (_isExactMatchFound == value) return;
            _isExactMatchFound = value;
            OnPropertyChanged();
        }
    }

    public bool IsCharExactMatchFound
    {
        get => _isCharExactMatchFound;
        set
        {
            if (_isCharExactMatchFound == value) return;
            _isCharExactMatchFound = value;
            OnPropertyChanged();
        }
    }

    private string? _selectedPiperVoiceToDownload;
    public ICommand ButtonDownloadPiperVoice { get; }
    public ICommand ButtonAddToBlackList { get; }
    public ICommand ButtonRemoveFromBlackList { get; }
    public ICommand ButtonAddWordReplacement { get; }
    public ICommand ButtonRemoveWordReplacement { get; }
    public ICommand ButtonAddCharReplacement { get; }
    public ICommand ButtonRemoveCharReplacement { get; }
    public string? _textBoxAddToBlackList { get; set; }
    public string? _textBoxWordToReplace;
    public string? _textBoxReplacementWord;
    public string? _textBoxCharToFind;
    public string? _textBoxCharReplacementWord;
    public ObservableCollection<string> ListBoxItemSourceBlacklist { get; set; } = new ObservableCollection<string>();

    public TtsTabViewModel(SettingsService settings)
    {
        _settings = settings;
        LoadAvailableVoices.GetListOfAvailableVoices(_availableVoices);
        ButtonDownloadPiperVoice = new RelayCommand(InstallPiperVoices);
        ButtonAddToBlackList = new RelayCommand(AddToBlackList);
        ButtonRemoveFromBlackList = new RelayCommand(RemoveFromBlackList);
        ButtonAddWordReplacement = new RelayCommand(AddToWordReplacement);
        ButtonRemoveWordReplacement = new RelayCommand(RemoveToWordReplacement);
        ButtonAddCharReplacement = new RelayCommand(AddCharReplacement);
        ButtonRemoveCharReplacement = new RelayCommand(RemoveCharReplacement);

        if (Design.IsDesignMode)
            return;

        _sliderSingleVoiceVolume = _settings.Settings.StandardVoiceVolume;
        _sliderSingleVoiceSpeed = _settings.Settings.StandardVoiceSpeed;
        _sliderCompressorPower = _settings.Settings.CompressorPower;
        _sliderMasterVolumeMultiplier = _settings.Settings.MasterVolumeMultiplier <= 0f ? 1.0 : Math.Round(_settings.Settings.MasterVolumeMultiplier, 1);
        _comboBoxSelectedItemInstalledVoices = _settings.Settings.SelectedSingleVoice;
        _checkBoxNormalizationTts = _settings.Settings.CheckBoxNormalizationTts;
        _checkBoxAllowSkipMessages = _settings.Settings.CheckBoxAllowSkipMessages;


        string voicesFilePath = PiperVoicesData.VoicesFilePath;
        if (File.Exists(voicesFilePath))
        {
            foreach (var item in File.ReadAllLines(voicesFilePath))
            {
                if (!string.IsNullOrWhiteSpace(item))
                    ComboBoxItemSourceAvailablePiperVoices.Add(item);
            }
        }
        foreach (var word in _settings.Settings.ListBoxItemSourceBlackList)
        {
            ListBoxItemSourceBlacklist.Add(word);
        }
        foreach (var kvp in _settings.Settings.WordReplacements)
        {
            _allWordReplacements.Add(new WordReplacementItemViewModel(kvp.Key, kvp.Value));
        }
        ApplyFilter();

        if (_settings.Settings.CharWordReplacements != null)
        {
            foreach (var kvp in _settings.Settings.CharWordReplacements)
            {
                _allCharWordReplacements.Add(new WordReplacementItemViewModel(kvp.Key, kvp.Value));
            }
        }
        ApplyCharFilter();
        if (_settings.Settings.SelectedSingleVoice == "" || _settings.Settings.SelectedSingleVoice == null)
        {
            try
            {

                _comboBoxSelectedItemInstalledVoices = new SpeechSynthesizer().GetInstalledVoices().FirstOrDefault()?.VoiceInfo.Name;
            }
            catch
            {
                Serilog.Log.Error("Нет голосов на компьютере");
            }
        }
        if (DoNotTtsIfStartWith == "" || DoNotTtsIfStartWith == null)
            DoNotTtsIfStartWith = "!";
        if (SkipMessage == "" || SkipMessage == null)
            SkipMessage = "!c";
        if (SkipAllMessages == "" || SkipAllMessages == null)
            SkipAllMessages = "!cв";
    }

    private void AddToWordReplacement()
    {
        var word = TextBoxWordToReplace?.Trim();
        var replacement = TextBoxReplacementWord?.Trim();

        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(replacement))
            return;

        // Поиск существующего слова без учета регистра
        var existingKey = _settings.Settings.WordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, word, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.WordReplacements.Remove(existingKey);
        }

        _settings.Settings.WordReplacements[word] = replacement;
        _settings.DelayedSave();

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
        OnPropertyChanged(nameof(TextBoxWordToReplace));
        OnPropertyChanged(nameof(TextBoxReplacementWord));
        ApplyFilter();
    }

    private void RemoveToWordReplacement()
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
            _settings.DelayedSave();
        }

        _allWordReplacements.Remove(itemToRemove);
        SelectedWordReplacementItem = null;

        TextBoxWordToReplace = string.Empty;
        TextBoxReplacementWord = string.Empty;
        OnPropertyChanged(nameof(TextBoxWordToReplace));
        OnPropertyChanged(nameof(TextBoxReplacementWord));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _isFiltering = true;
        try
        {
            string queryWord = _textBoxWordToReplace?.Trim() ?? string.Empty;
            string queryRep = _textBoxReplacementWord?.Trim() ?? string.Empty;

            bool hasWordQuery = !string.IsNullOrEmpty(queryWord);
            bool hasRepQuery = !string.IsNullOrEmpty(queryRep);

            FilteredWordReplacements.Clear();
            bool anyExact = false;

            foreach (var item in _allWordReplacements)
            {
                bool wordContains = !hasWordQuery || item.Word.Contains(queryWord, StringComparison.OrdinalIgnoreCase);
                bool repContains = !hasRepQuery || item.Replacement.Contains(queryRep, StringComparison.OrdinalIgnoreCase);

                bool isMatch;
                if (hasWordQuery && hasRepQuery)
                {
                    isMatch = wordContains && repContains;
                }
                else if (hasWordQuery)
                {
                    isMatch = wordContains;
                }
                else if (hasRepQuery)
                {
                    isMatch = repContains;
                }
                else
                {
                    isMatch = true;
                }

                if (isMatch)
                {
                    bool isWordExact = hasWordQuery && string.Equals(item.Word, queryWord, StringComparison.OrdinalIgnoreCase);
                    bool isRepExact = hasRepQuery && string.Equals(item.Replacement, queryRep, StringComparison.OrdinalIgnoreCase);

                    item.IsWordExactMatch = isWordExact;
                    item.IsReplacementExactMatch = isRepExact;

                    if (isWordExact || isRepExact)
                    {
                        anyExact = true;
                    }

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
        finally
        {
            _isFiltering = false;
        }
    }

    private void AddCharReplacement()
    {
        var symbol = TextBoxCharToFind?.Trim();
        var replacement = TextBoxCharReplacementWord?.Trim();

        if (string.IsNullOrWhiteSpace(symbol))
            return;

        if (string.IsNullOrWhiteSpace(replacement))
        {
            replacement = symbol;
        }

        var existingKey = _settings.Settings.CharWordReplacements.Keys
            .FirstOrDefault(k => string.Equals(k, symbol, StringComparison.OrdinalIgnoreCase));

        if (existingKey != null)
        {
            _settings.Settings.CharWordReplacements.Remove(existingKey);
        }

        _settings.Settings.CharWordReplacements[symbol] = replacement;
        _settings.DelayedSave();

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
        OnPropertyChanged(nameof(TextBoxCharToFind));
        OnPropertyChanged(nameof(TextBoxCharReplacementWord));
        ApplyCharFilter();
    }

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
            _settings.DelayedSave();
        }

        _allCharWordReplacements.Remove(itemToRemove);
        SelectedCharReplacementItem = null;

        TextBoxCharToFind = string.Empty;
        TextBoxCharReplacementWord = string.Empty;
        OnPropertyChanged(nameof(TextBoxCharToFind));
        OnPropertyChanged(nameof(TextBoxCharReplacementWord));
        ApplyCharFilter();
    }

    private void ApplyCharFilter()
    {
        _isCharFiltering = true;
        try
        {
            string querySymbol = _textBoxCharToFind?.Trim() ?? string.Empty;
            string queryRep = _textBoxCharReplacementWord?.Trim() ?? string.Empty;

            bool hasSymbolQuery = !string.IsNullOrEmpty(querySymbol);
            bool hasRepQuery = !string.IsNullOrEmpty(queryRep);

            FilteredCharWordReplacements.Clear();
            bool anyExact = false;

            foreach (var item in _allCharWordReplacements)
            {
                bool symbolContains = !hasSymbolQuery || item.Word.Contains(querySymbol, StringComparison.OrdinalIgnoreCase);
                bool repContains = !hasRepQuery || item.Replacement.Contains(queryRep, StringComparison.OrdinalIgnoreCase);

                bool isMatch;
                if (hasSymbolQuery && hasRepQuery)
                {
                    isMatch = symbolContains && repContains;
                }
                else if (hasSymbolQuery)
                {
                    isMatch = symbolContains;
                }
                else if (hasRepQuery)
                {
                    isMatch = repContains;
                }
                else
                {
                    isMatch = true;
                }

                if (isMatch)
                {
                    bool isSymbolExact = hasSymbolQuery && string.Equals(item.Word, querySymbol, StringComparison.OrdinalIgnoreCase);
                    bool isRepExact = hasRepQuery && string.Equals(item.Replacement, queryRep, StringComparison.OrdinalIgnoreCase);

                    item.IsWordExactMatch = isSymbolExact;
                    item.IsReplacementExactMatch = isRepExact;

                    if (isSymbolExact || isRepExact)
                    {
                        anyExact = true;
                    }

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
        finally
        {
            _isCharFiltering = false;
        }
    }

    public ObservableCollection<string> ComboBoxItemSourceAvailablePiperVoices { get; set; } = new ObservableCollection<string>();
    public ObservableCollection<string> ComboBoxItemSourceInstalledVoices
    {
        get => _availableVoices;
        set { _availableVoices = value; OnPropertyChanged(); }
    }

    public string? ComboBoxSelectedItemInstalledVoices
    {
        get => _comboBoxSelectedItemInstalledVoices;
        set
        {
            _comboBoxSelectedItemInstalledVoices = value;
            OnPropertyChanged();
            _settings.Settings.SelectedSingleVoice = value;
            _settings.DelayedSave();
        }
    }
    private int _selectedWordReplacementIndex = -1;
    public int SelectedWordReplacementIndex
    {
        get => _selectedWordReplacementIndex;
        set { _selectedWordReplacementIndex = value; OnPropertyChanged(); }
    }
    public string DoNotTtsIfStartWith
    {
        get => _settings.Settings.DoNotTtsIfStartWith;
        set
        {
            _settings.Settings.DoNotTtsIfStartWith = value;
            OnPropertyChanged();
            _settings.DelayedSave();
        }
    }

    public string SkipMessage
    {
        get => _settings.Settings.SkipMessage;
        set
        {
            _settings.Settings.SkipMessage = value;
            OnPropertyChanged();
            _settings.DelayedSave();
        }
    }

    public string SkipAllMessages
    {
        get => _settings.Settings.SkipAllMessages;
        set
        {
            _settings.Settings.SkipAllMessages = value;
            OnPropertyChanged();
            _settings.DelayedSave();
        }
    }

    public int SliderSingleVoiceVolume
    {
        get => _sliderSingleVoiceVolume;
        set
        {
            _sliderSingleVoiceVolume = value;
            OnPropertyChanged();
            _settings.Settings.StandardVoiceVolume = value;
            _settings.DelayedSave();
        }
    }

    public int SliderSingleVoiceSpeed
    {
        get => _sliderSingleVoiceSpeed;
        set
        {
            _sliderSingleVoiceSpeed = value;
            OnPropertyChanged();
            _settings.Settings.StandardVoiceSpeed = value;
            _settings.DelayedSave();
        }
    }

    public int SliderCompressorPower
    {
        get => _sliderCompressorPower;
        set
        {
            _sliderCompressorPower = value;
            OnPropertyChanged();
            _settings.Settings.CompressorPower = value;
            _settings.DelayedSave();
        }
    }

    public double SliderMasterVolumeMultiplier
    {
        get => _sliderMasterVolumeMultiplier;
        set
        {
            var rounded = Math.Round(value, 1);
            if (Math.Abs(_sliderMasterVolumeMultiplier - rounded) < 0.01)
                return;

            _sliderMasterVolumeMultiplier = rounded;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterVolumeMultiplierText));
            _settings.Settings.MasterVolumeMultiplier = (float)rounded;
            _settings.DelayedSave();
        }
    }

    public string MasterVolumeMultiplierText => $"x{_sliderMasterVolumeMultiplier:F1}";

    public string? TextBoxWordToReplace
    {
        get => _textBoxWordToReplace;
        set
        {
            if (_textBoxWordToReplace == value)
                return;
            _textBoxWordToReplace = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }
    public string? TextBoxReplacementWord
    {
        get => _textBoxReplacementWord;
        set
        {
            if (_textBoxReplacementWord == value)
                return;
            _textBoxReplacementWord = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }
    public string? TextBoxCharToFind
    {
        get => _textBoxCharToFind;
        set
        {
            if (_textBoxCharToFind == value)
                return;
            _textBoxCharToFind = value;
            OnPropertyChanged();
            ApplyCharFilter();
        }
    }
    public string? TextBoxCharReplacementWord
    {
        get => _textBoxCharReplacementWord;
        set
        {
            if (_textBoxCharReplacementWord == value)
                return;
            _textBoxCharReplacementWord = value;
            OnPropertyChanged();
            ApplyCharFilter();
        }
    }
    public string? TextBoxAddToBlackList
    {
        get => _textBoxAddToBlackList;
        set { _textBoxAddToBlackList = value; }
    }
    public bool CheckBoxNormalizationTts
    {
        get => _checkBoxNormalizationTts;
        set
        {
            _checkBoxNormalizationTts = value;
            _settings.Settings.CheckBoxNormalizationTts = value;
            _settings.DelayedSave();
        }
    }
    public bool CheckBoxAllowSkipMessages
    {
        get => _checkBoxAllowSkipMessages;
        set
        {
            _checkBoxAllowSkipMessages = value;
            _settings.Settings.CheckBoxAllowSkipMessages = value;
            _settings.DelayedSave();
        }
    }
    private bool _isPiperDownloading;
    public bool IsPiperDownloading
    {
        get => _isPiperDownloading;
        set { _isPiperDownloading = value; OnPropertyChanged(); }
    }

    private bool _isPiperDownloadProgressVisible;
    public bool IsPiperDownloadProgressVisible
    {
        get => _isPiperDownloadProgressVisible;
        set { _isPiperDownloadProgressVisible = value; OnPropertyChanged(); }
    }

    private double _piperDownloadProgressValue;
    public double PiperDownloadProgressValue
    {
        get => _piperDownloadProgressValue;
        set { _piperDownloadProgressValue = value; OnPropertyChanged(); }
    }

    private bool _isPiperDownloadIndeterminate;
    public bool IsPiperDownloadIndeterminate
    {
        get => _isPiperDownloadIndeterminate;
        set { _isPiperDownloadIndeterminate = value; OnPropertyChanged(); }
    }

    private string _piperDownloadStatusText = string.Empty;
    public string PiperDownloadStatusText
    {
        get => _piperDownloadStatusText;
        set { _piperDownloadStatusText = value; OnPropertyChanged(); }
    }

    private string _piperDownloadProgressText = string.Empty;
    public string PiperDownloadProgressText
    {
        get => _piperDownloadProgressText;
        set { _piperDownloadProgressText = value; OnPropertyChanged(); }
    }

    private IBrush _piperDownloadStatusBrush = Brushes.Gray;
    public IBrush PiperDownloadStatusBrush
    {
        get => _piperDownloadStatusBrush;
        set { _piperDownloadStatusBrush = value; OnPropertyChanged(); }
    }

    public string? SelectedPiperVoiceToDownload
    {
        get => _selectedPiperVoiceToDownload;
        set
        {
            _selectedPiperVoiceToDownload = value;
            OnPropertyChanged();
            if (!_isPiperDownloading && _isPiperDownloadProgressVisible)
            {
                IsPiperDownloadProgressVisible = false;
                PiperDownloadStatusText = string.Empty;
                PiperDownloadProgressText = string.Empty;
                PiperDownloadProgressValue = 0;
            }
        }
    }

    private async void InstallPiperVoices()
    {
        var piperVoiceName = SelectedPiperVoiceToDownload;
        if (string.IsNullOrWhiteSpace(piperVoiceName))
            return;

        if (IsPiperDownloading)
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

                if (!_availableVoices.Contains(piperVoiceName))
                {
                    _availableVoices.Add(piperVoiceName);
                }
            }
            else if (result.Status == PiperDownloadStatus.AlreadyExists)
            {
                PiperDownloadProgressValue = 100;
                PiperDownloadProgressText = "";
                PiperDownloadStatusText = "Голос уже установлен";
                PiperDownloadStatusBrush = new SolidColorBrush(Color.Parse("#42A5F5"));

                if (!_availableVoices.Contains(piperVoiceName))
                {
                    _availableVoices.Add(piperVoiceName);
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
            Serilog.Log.Error(ex, "Ошибка при загрузке голоса Piper: {VoiceName}", piperVoiceName);
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
    private string? _selectedBlacklistItem;
    public string? SelectedBlacklistItem
    {
        get => _selectedBlacklistItem;
        set { _selectedBlacklistItem = value; OnPropertyChanged(); }
    }
    private void AddToBlackList()
    {
        string? username = TextBoxAddToBlackList?.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return;

        if (!ListBoxItemSourceBlacklist.Any(x => string.Equals(x, username, StringComparison.OrdinalIgnoreCase)))
        {
            ListBoxItemSourceBlacklist.Insert(0, username);
            _settings.Settings.ListBoxItemSourceBlackList = ListBoxItemSourceBlacklist.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _settings.DelayedSave();
        }

        TextBoxAddToBlackList = string.Empty;
    }
    private void RemoveFromBlackList()
    {
        if (SelectedBlacklistItem == null)
            return;

        ListBoxItemSourceBlacklist.Remove(SelectedBlacklistItem);
        _settings.Settings.ListBoxItemSourceBlackList = ListBoxItemSourceBlacklist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _settings.DelayedSave();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}