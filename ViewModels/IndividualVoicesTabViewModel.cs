using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public class IndividualVoicesTabViewModel : INotifyPropertyChanged
{
    private readonly IndividualVoiceService _individualVoiceService;

    // ============================================
    // ВСЕ ДОСТУПНЫЕ ГОЛОСА
    // ============================================

    public ObservableCollection<IndividualVoiceSettings>
        IndividualVoices =>
        _individualVoiceService.Voices;


    // ============================================
    // ТОЛЬКО ВКЛЮЧЁННЫЕ ГОЛОСА
    // ============================================

    public ObservableCollection<IndividualVoiceSettings>
        EnabledVoices
    { get; }
        = new();


    // ============================================
    // ПРИВЯЗКИ ПОЛЬЗОВАТЕЛЕЙ
    // ============================================

    public ObservableCollection<IndividualVoiceAssignment>
        IndividualVoiceAssignments =>
        _individualVoiceService.Assignments;


    // ============================================
    // КОНСТРУКТОР
    // ============================================

    public IndividualVoicesTabViewModel(
        IndividualVoiceService individualVoiceService)
    {
        _individualVoiceService =
            individualVoiceService;

        // Следим за появлением/удалением пользователей
        _individualVoiceService.Assignments.CollectionChanged +=
            Assignments_CollectionChanged;

        // Подписываемся на уже существующие привязки
        // (важно и для сохранения смены голоса, и для поиска)
        foreach (var assignment in IndividualVoiceAssignments)
        {
            assignment.PropertyChanged +=
                Assignment_PropertyChanged;
        }

        if (Design.IsDesignMode)
            return;

        // Загружаем голоса
        LoadIndividualVoices();

        // Создаём список включённых голосов
        UpdateEnabledVoices();

        // Привязываем загруженные VoiceName
        // к объектам IndividualVoiceSettings
        InitializeSelectedVoices();

        // Заполняем список для UI сразу при старте
        RefreshFilteredAssignments();
    }


    // ============================================
    // ЗАГРУЗКА ГОЛОСОВ
    // ============================================

    private void LoadIndividualVoices()
    {
        var saved =
            VoiceSettingsStorage.Load();

        using var synth =
            new System.Speech.Synthesis.SpeechSynthesizer();


        // ========================================
        // СИСТЕМНЫЕ ГОЛОСА WINDOWS
        // ========================================

        foreach (
            System.Speech.Synthesis.InstalledVoice voice
            in synth.GetInstalledVoices())
        {
            AddVoice(
                voice.VoiceInfo.Name,
                saved);
        }


        // ========================================
        // ГОЛОСА ИЗ MODELS
        // ========================================

        var modelsPath =
            System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(),
                "models");

        if (System.IO.Directory.Exists(modelsPath))
        {
            var folderNames =
                System.IO.Directory
                    .GetDirectories(modelsPath)
                    .Select(System.IO.Path.GetFileName);

            foreach (var folder in folderNames)
            {
                if (folder != null)
                {
                    AddVoice(
                        folder,
                        saved);
                }
            }
        }
    }


    // ============================================
    // ДОБАВЛЕНИЕ ГОЛОСА
    // ============================================

    private void AddVoice(
        string name,
        Dictionary<string, VoiceSettingsDto> saved)
    {
        var settings =
            saved.TryGetValue(
                name,
                out var dto)
                ? new IndividualVoiceSettings(
                    name,
                    dto.Volume,
                    dto.Speed,
                    dto.IsEnabled)
                : new IndividualVoiceSettings(name);


        settings.PropertyChanged +=
            VoiceSettings_PropertyChanged;


        _individualVoiceService.AddVoice(
            settings);
    }


    // ============================================
    // ИЗМЕНЕНИЕ НАСТРОЕК ГОЛОСА
    // ============================================

    private void VoiceSettings_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(IndividualVoiceSettings.IsEnabled))
        {
            UpdateEnabledVoices();

            // Если список доступных голосов изменился,
            // заново проверяем выбранные голоса.
            InitializeSelectedVoices();
        }
    }


    // ============================================
    // ОБНОВЛЕНИЕ СПИСКА ВКЛЮЧЁННЫХ ГОЛОСОВ
    // ============================================

    private void UpdateEnabledVoices()
    {
        EnabledVoices.Clear();

        foreach (var voice in IndividualVoices)
        {
            if (voice.IsEnabled)
            {
                EnabledVoices.Add(voice);
            }
        }

        OnPropertyChanged(
            nameof(EnabledVoices));
    }



    // ============================================
    // ВОССТАНОВЛЕНИЕ ВЫБРАННЫХ ГОЛОСОВ
    // ============================================

    private void InitializeSelectedVoices()
    {
        foreach (var assignment in IndividualVoiceAssignments)
        {
            InitializeSelectedVoice(assignment);
        }
    }

    private void InitializeSelectedVoice(
        IndividualVoiceAssignment assignment)
    {
        var voice =
            EnabledVoices.FirstOrDefault(
                x => string.Equals(
                    x.Name,
                    assignment.VoiceName,
                    StringComparison.OrdinalIgnoreCase));

        // Временно отписываемся, чтобы восстановление
        // сохранённого голоса не воспринималось как
        // ручной выбор пользователя и не дёргало сохранение.
        assignment.PropertyChanged -= Assignment_PropertyChanged;
        assignment.SelectedVoice = voice;
        assignment.PropertyChanged += Assignment_PropertyChanged;
    }

    private void Assignments_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (IndividualVoiceAssignment assignment in e.NewItems)
            {
                assignment.PropertyChanged +=
                    Assignment_PropertyChanged;

                InitializeSelectedVoice(assignment);
            }
        }

        if (e.OldItems != null)
        {
            foreach (IndividualVoiceAssignment assignment in e.OldItems)
            {
                assignment.PropertyChanged -=
                    Assignment_PropertyChanged;
            }
        }

        RefreshFilteredAssignments();
    }


    // ============================================
    // РЕАКЦИЯ НА РУЧНОЙ ВЫБОР ГОЛОСА В UI
    // ============================================

    private void Assignment_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IndividualVoiceAssignment.SelectedVoice))
            return;

        if (sender is not IndividualVoiceAssignment assignment)
            return;

        if (assignment.SelectedVoice == null)
            return;

        // Именно этот вызов обновляет _assignments,
        // VoiceName и сохраняет json.
        _individualVoiceService.ChangeAssignmentVoice(
            assignment,
            assignment.SelectedVoice.Name);

        Dispatcher.UIThread.Post(
    RefreshFilteredAssignments,
    DispatcherPriority.Background);
    }


    // ============================================
    // ИЗМЕНЕНИЕ ГОЛОСА ПОЛЬЗОВАТЕЛЯ (программно)
    // ============================================

    public void ChangeAssignmentVoice(
        IndividualVoiceAssignment assignment,
        string? voiceName)
    {
        if (assignment == null)
            return;

        if (string.IsNullOrWhiteSpace(voiceName))
            return;


        var voice =
            IndividualVoices.FirstOrDefault(
                x => string.Equals(
                    x.Name,
                    voiceName,
                    StringComparison.OrdinalIgnoreCase));


        if (voice == null)
            return;


        // Нельзя выбрать выключенный голос
        if (!voice.IsEnabled)
            return;


        _individualVoiceService.ChangeAssignmentVoice(
            assignment,
            voiceName);


        // Обновляем выбранный объект для ComboBox
        // (отписка/подписка — чтобы не задвоить сохранение,
        // ChangeAssignmentVoice уже сохранил json)
        assignment.PropertyChanged -= Assignment_PropertyChanged;
        assignment.SelectedVoice = voice;
        assignment.PropertyChanged += Assignment_PropertyChanged;

        RefreshFilteredAssignments();
    }


    // ============================================
    // ПОИСК
    // ============================================

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshFilteredAssignments();
        }
    }

    public ObservableCollection<IndividualVoiceAssignment>
        FilteredIndividualVoiceAssignments
    { get; }
        = new();

    private void RefreshFilteredAssignments()
    {
        FilteredIndividualVoiceAssignments.Clear();

        IEnumerable<IndividualVoiceAssignment> query =
            IndividualVoiceAssignments;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = IndividualVoiceAssignments.Where(a =>
                (a.Platform?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Username?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.SelectedVoice?.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var item in query)
            FilteredIndividualVoiceAssignments.Add(item);
    }


    // ============================================
    // PROPERTY CHANGED
    // ============================================

    public event PropertyChangedEventHandler?
        PropertyChanged;


    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }

}