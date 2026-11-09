using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Services;

namespace TtsStreamSupporter.ViewModels;

public partial class IndividualVoicesTabViewModel : ViewModelBase
{
    private readonly IndividualVoiceService _individualVoiceService;
    private readonly IVoiceDiscoveryService _voiceDiscoveryService;

    public ObservableCollection<IndividualVoiceSettings> IndividualVoices => _individualVoiceService.Voices;
    public ObservableCollection<IndividualVoiceSettings> EnabledVoices { get; } = new();
    public ObservableCollection<IndividualVoiceAssignment> IndividualVoiceAssignments => _individualVoiceService.Assignments;
    public ObservableCollection<IndividualVoiceAssignment> FilteredIndividualVoiceAssignments { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    public IndividualVoicesTabViewModel(
        IndividualVoiceService individualVoiceService,
        IVoiceDiscoveryService? voiceDiscoveryService = null)
    {
        _individualVoiceService = individualVoiceService;
        _voiceDiscoveryService = voiceDiscoveryService ?? new VoiceDiscoveryService();

        _individualVoiceService.Assignments.CollectionChanged += Assignments_CollectionChanged;

        foreach (var assignment in IndividualVoiceAssignments)
        {
            assignment.PropertyChanged += Assignment_PropertyChanged;
        }

        if (Design.IsDesignMode)
            return;

        LoadIndividualVoices();
        UpdateEnabledVoices();
        InitializeSelectedVoices();
        RefreshFilteredAssignments();
    }

    private void LoadIndividualVoices()
    {
        var saved = VoiceSettingsStorage.Load();
        var discoveredVoices = _voiceDiscoveryService.GetAvailableVoices();

        foreach (var voiceName in discoveredVoices)
        {
            AddVoice(voiceName, saved);
        }
    }

    private void AddVoice(string name, Dictionary<string, VoiceSettingsDto> saved)
    {
        // Избегаем дубликатов, если голос уже добавлен в сервис
        if (IndividualVoices.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        var settings = saved.TryGetValue(name, out var dto)
            ? new IndividualVoiceSettings(name, dto.Volume, dto.Speed, dto.IsEnabled)
            : new IndividualVoiceSettings(name);

        settings.PropertyChanged += VoiceSettings_PropertyChanged;
        _individualVoiceService.AddVoice(settings);
    }

    private void VoiceSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IndividualVoiceSettings.IsEnabled))
        {
            UpdateEnabledVoices();
            InitializeSelectedVoices();
        }
    }

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
    }

    private void InitializeSelectedVoices()
    {
        foreach (var assignment in IndividualVoiceAssignments)
        {
            InitializeSelectedVoice(assignment);
        }
    }

    private void InitializeSelectedVoice(IndividualVoiceAssignment assignment)
    {
        var voice = EnabledVoices.FirstOrDefault(
            x => string.Equals(x.Name, assignment.VoiceName, StringComparison.OrdinalIgnoreCase));

        assignment.PropertyChanged -= Assignment_PropertyChanged;
        assignment.SelectedVoice = voice;
        assignment.PropertyChanged += Assignment_PropertyChanged;
    }

    private void Assignments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (IndividualVoiceAssignment assignment in e.NewItems)
            {
                assignment.PropertyChanged += Assignment_PropertyChanged;
                InitializeSelectedVoice(assignment);
            }
        }

        if (e.OldItems != null)
        {
            foreach (IndividualVoiceAssignment assignment in e.OldItems)
            {
                assignment.PropertyChanged -= Assignment_PropertyChanged;
            }
        }

        RefreshFilteredAssignments();
    }

    private void Assignment_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IndividualVoiceAssignment.SelectedVoice))
            return;

        if (sender is not IndividualVoiceAssignment assignment || assignment.SelectedVoice == null)
            return;

        _individualVoiceService.ChangeAssignmentVoice(assignment, assignment.SelectedVoice.Name);

        Dispatcher.UIThread.Post(RefreshFilteredAssignments, DispatcherPriority.Background);
    }

    public void ChangeAssignmentVoice(IndividualVoiceAssignment assignment, string? voiceName)
    {
        if (assignment == null || string.IsNullOrWhiteSpace(voiceName))
            return;

        var voice = IndividualVoices.FirstOrDefault(
            x => string.Equals(x.Name, voiceName, StringComparison.OrdinalIgnoreCase));

        if (voice == null || !voice.IsEnabled)
            return;

        _individualVoiceService.ChangeAssignmentVoice(assignment, voiceName);

        assignment.PropertyChanged -= Assignment_PropertyChanged;
        assignment.SelectedVoice = voice;
        assignment.PropertyChanged += Assignment_PropertyChanged;

        RefreshFilteredAssignments();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredAssignments();
    }

    private void RefreshFilteredAssignments()
    {
        FilteredIndividualVoiceAssignments.Clear();

        IEnumerable<IndividualVoiceAssignment> query = IndividualVoiceAssignments;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = IndividualVoiceAssignments.Where(a =>
                (a.Platform?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Username?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.SelectedVoice?.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var item in query)
        {
            FilteredIndividualVoiceAssignments.Add(item);
        }
    }
}
