using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    public ConnectionsTabViewModel ConnectionsTab { get; }
    public TtsTabViewModel TtsTab { get; }
    public IndividualVoicesTabViewModel IndividualVoicesTab { get; }
    public LogsTabViewModel LogsTab { get; }
    public NttsTabViewModel NttsTab { get; }
    public NttsHealthCheckService NttsHealthCheck { get; }

    // Подключения для отображения в правой нижней части MainWindow
    public ObservableCollection<ConnectionStatus> Connections => ConnectionsTab.Connections;

    public ObservableCollection<AppWarningViewModel> ActiveWarnings { get; } = new();

    [ObservableProperty]
    private string _statusText = "Кнопка ещё не нажата";

    [ObservableProperty]
    private bool _individualVoicesEnabled;

    public bool CheckBoxNttsVoicesIsEnabled
    {
        get => NttsTab.CheckBoxNttsVoicesIsEnabled;
        set
        {
            if (NttsTab.CheckBoxNttsVoicesIsEnabled != value)
            {
                NttsTab.CheckBoxNttsVoicesIsEnabled = value;
                OnPropertyChanged();
                UpdateWarnings();
            }
        }
    }

    public MainWindowViewModel(
        SettingsService settings,
        ConnectionsTabViewModel connectionsTab,
        TtsTabViewModel ttsTab,
        IndividualVoicesTabViewModel individualVoicesTab,
        LogsTabViewModel logsTab,
        NttsTabViewModel nttsTab,
        IChatRouterService chatRouterService,
        NttsHealthCheckService nttsHealthCheck)
    {
        _settings = settings;

        ConnectionsTab = connectionsTab;
        TtsTab = ttsTab;
        IndividualVoicesTab = individualVoicesTab;
        LogsTab = logsTab;
        NttsTab = nttsTab;
        NttsHealthCheck = nttsHealthCheck;

        // Запуск фоновой маршрутизации сообщений чата
        chatRouterService.Initialize();

        _individualVoicesEnabled = _settings.Settings.IndividualVoicesEnabled;

        // Отслеживание изменений NTTS таба
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

        // Отслеживание изменений TTS таба
        TtsTab.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TtsTab.ComboBoxSelectedItemInstalledVoices) ||
                args.PropertyName == nameof(TtsTab.SliderSingleVoiceVolume))
            {
                UpdateWarnings();
            }
        };

        // Отслеживание изменений индивидуальных голосов
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

    partial void OnIndividualVoicesEnabledChanged(bool value)
    {
        _settings.Settings.IndividualVoicesEnabled = value;
        _ = _settings.DelayedSave();
        UpdateWarnings();
    }

    private void UpdateWarnings()
    {
        ActiveWarnings.Clear();

        // 1. Одиночный голос: не установлен или громкость равна нулю
        bool voiceMissing = string.IsNullOrWhiteSpace(TtsTab.ComboBoxSelectedItemInstalledVoices);
        bool volumeZero = TtsTab.SliderSingleVoiceVolume <= 0;

        if (voiceMissing && volumeZero)
        {
            ActiveWarnings.Add(new AppWarningViewModel("Одиночный голос не выбран, громкость 0", "#D32F2F"));
        }
        else if (voiceMissing)
        {
            ActiveWarnings.Add(new AppWarningViewModel("Одиночный голос не выбран", "#E65100"));
        }
        else if (volumeZero)
        {
            ActiveWarnings.Add(new AppWarningViewModel("Громкость одиночного голоса равна 0", "#E65100"));
        }

        // 2. Индивидуальные голоса: включены, но ни один не выбран
        if (IndividualVoicesEnabled)
        {
            bool anyEnabled = IndividualVoicesTab.IndividualVoices.Any(v => v.IsEnabled);
            if (!anyEnabled)
            {
                ActiveWarnings.Add(new AppWarningViewModel("Включены индивидуальные голоса, но ни один не выбран", "#D32F2F"));
            }
        }

        // 3. NTTS голоса: включены, но не выставлен API-ключ
        if (CheckBoxNttsVoicesIsEnabled)
        {
            if (string.IsNullOrWhiteSpace(NttsTab.NttsApi))
            {
                ActiveWarnings.Add(new AppWarningViewModel("Включены NTTS голоса, но не указан API-ключ", "#D32F2F"));
            }
        }
    }
}
