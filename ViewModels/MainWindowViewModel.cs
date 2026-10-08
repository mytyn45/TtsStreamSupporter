using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;

    public MainWindowViewModel(
        SettingsService settings,
        ConnectionsTabViewModel connectionsTab,
        TtsTabViewModel ttsTab,
        IndividualVoicesTabViewModel individualVoicesTab,
        LogsTabViewModel logsTab,
        NttsTabViewModel nttsTab,
        TtsMessageProcessing ttsMessageProcessor,
        MessageQueue msqQueue,
        SpeechPlayerService speechPlayerService,
        TwitchConnection twitchConnection,
        GoodgameConnection goodgameConnection,
        BluestarConnection bluestarConnection)
    {
        _settings = settings;

        ConnectionsTab = connectionsTab;
        TtsTab = ttsTab;
        IndividualVoicesTab = individualVoicesTab;
        LogsTab = logsTab;
        NttsTab = nttsTab;
        TtsMessageProcess = ttsMessageProcessor;
        MessageQueue = msqQueue;
        SpeechPlayerSerive = speechPlayerService;
        TwitchConnection = twitchConnection;
        GoodgameConnection = goodgameConnection;
        BluestarConnection = bluestarConnection;

        _individualVoicesEnabled =
            _settings.Settings.IndividualVoicesEnabled;

        NttsTab.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(NttsTab.CheckBoxNttsVoicesIsEnabled))
            {
                OnPropertyChanged(nameof(CheckBoxNttsVoicesIsEnabled));
                UpdateWarnings();
            }
            else if (args.PropertyName == nameof(NttsTab.NttsApi))
            {
                UpdateWarnings();
            }
        };

        TtsTab.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TtsTab.ComboBoxSelectedItemInstalledVoices) ||
                args.PropertyName == nameof(TtsTab.SliderSingleVoiceVolume))
            {
                UpdateWarnings();
            }
        };

        void SubscribeVoice(IndividualVoiceSettings v)
        {
            v.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IndividualVoiceSettings.IsEnabled))
                {
                    UpdateWarnings();
                }
            };
        }

        foreach (var voice in IndividualVoicesTab.IndividualVoices)
        {
            SubscribeVoice(voice);
        }

        IndividualVoicesTab.IndividualVoices.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (IndividualVoiceSettings v in e.NewItems)
                    SubscribeVoice(v);
            }
            UpdateWarnings();
        };

        IndividualVoicesTab.EnabledVoices.CollectionChanged += (s, e) => UpdateWarnings();

        UpdateWarnings();
    }

    private string _statusText = "Кнопка ещё не нажата";
    private bool _individualVoicesEnabled;

    public ObservableCollection<AppWarning> ActiveWarnings { get; } = new();

    public bool IndividualVoicesEnabled
    {
        get => _individualVoicesEnabled;

        set
        {
            if (_individualVoicesEnabled == value)
                return;

            _individualVoicesEnabled = value;

            OnPropertyChanged();

            _settings.Settings.IndividualVoicesEnabled = value;
            _ = _settings.DelayedSave();
            UpdateWarnings();
        }
    }

    public bool CheckBoxNttsVoicesIsEnabled
    {
        get => NttsTab.CheckBoxNttsVoicesIsEnabled;

        set
        {
            if (NttsTab.CheckBoxNttsVoicesIsEnabled == value)
                return;

            NttsTab.CheckBoxNttsVoicesIsEnabled = value;
            OnPropertyChanged();
            UpdateWarnings();
        }
    }

    private void UpdateWarnings()
    {
        ActiveWarnings.Clear();

        // 1. Одиночный голос: не установлен или громкость равна нулю
        bool voiceMissing = string.IsNullOrWhiteSpace(TtsTab.ComboBoxSelectedItemInstalledVoices);
        bool volumeZero = TtsTab.SliderSingleVoiceVolume <= 0;

        if (voiceMissing && volumeZero)
        {
            ActiveWarnings.Add(new AppWarning("Одиночный голос не выбран, громкость 0", "#D32F2F"));
        }
        else if (voiceMissing)
        {
            ActiveWarnings.Add(new AppWarning("Одиночный голос не выбран", "#E65100"));
        }
        else if (volumeZero)
        {
            ActiveWarnings.Add(new AppWarning("Громкость одиночного голоса равна 0", "#E65100"));
        }

        // 2. Индивидуальные голоса: включены, но ни один не выбран
        if (IndividualVoicesEnabled)
        {
            bool anyEnabled = IndividualVoicesTab.IndividualVoices.Any(v => v.IsEnabled);
            if (!anyEnabled)
            {
                ActiveWarnings.Add(new AppWarning("Включены индивидуальные голоса, но ни один не выбран", "#D32F2F"));
            }
        }

        // 3. NTTS голоса: включены, но не выставлен API-ключ
        if (CheckBoxNttsVoicesIsEnabled)
        {
            if (string.IsNullOrWhiteSpace(NttsTab.NttsApi))
            {
                ActiveWarnings.Add(new AppWarning("Включены NTTS голоса, но не указан API-ключ", "#D32F2F"));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;

        set
        {
            if (_statusText == value)
                return;

            _statusText = value;

            OnPropertyChanged();
        }
    }

    public ConnectionsTabViewModel ConnectionsTab { get; }
    public TtsTabViewModel TtsTab { get; }
    public LogsTabViewModel LogsTab { get; }
    public IndividualVoicesTabViewModel IndividualVoicesTab { get; }
    public NttsTabViewModel NttsTab { get; }
    public TtsMessageProcessing TtsMessageProcess { get; }
    public MessageQueue MessageQueue { get; }
    public SpeechPlayerService SpeechPlayerSerive { get; }
    public TwitchConnection TwitchConnection { get; }
    public GoodgameConnection GoodgameConnection { get; }
    public BluestarConnection BluestarConnection { get; }

    // Подключения для отображения в MainWindow
    public ObservableCollection<ConnectionStatus> Connections =>
        ConnectionsTab.Connections;

    public ObservableCollection<string> BlacklistedUsers { get; }
        = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}