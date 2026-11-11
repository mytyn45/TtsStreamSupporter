using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TtsStreamSupporter.ViewModels;

public partial class WordReplacementItemViewModel : ViewModelBase
{
    private static readonly IBrush MatchBackground = Brush.Parse("#BBDEFB");
    private static readonly IBrush MatchBorderBrush = Brush.Parse("#1976D2");
    private static readonly IBrush MatchForeground = Brush.Parse("#0D47A1");

    private static readonly IBrush ConflictBackground = Brush.Parse("#FFF3E0");
    private static readonly IBrush ConflictBorderBrush = Brush.Parse("#FF9800");

    [ObservableProperty]
    private string _word;

    [ObservableProperty]
    private string _replacement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordBackground))]
    [NotifyPropertyChangedFor(nameof(WordForeground))]
    [NotifyPropertyChangedFor(nameof(WordBorderBrush))]
    [NotifyPropertyChangedFor(nameof(WordBorderThickness))]
    [NotifyPropertyChangedFor(nameof(WordFontWeight))]
    private bool _isWordExactMatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacementBackground))]
    [NotifyPropertyChangedFor(nameof(ReplacementForeground))]
    [NotifyPropertyChangedFor(nameof(ReplacementBorderBrush))]
    [NotifyPropertyChangedFor(nameof(ReplacementBorderThickness))]
    [NotifyPropertyChangedFor(nameof(ReplacementFontWeight))]
    private bool _isReplacementExactMatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordBackground))]
    [NotifyPropertyChangedFor(nameof(WordBorderBrush))]
    [NotifyPropertyChangedFor(nameof(WordBorderThickness))]
    private bool _isConflict;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConflictBadgeText))]
    private string? _conflictWith;

    public string ConflictBadgeText => string.IsNullOrWhiteSpace(ConflictWith)
        ? "Конфликт"
        : $"Конфликт с '{ConflictWith}'";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreatedAtText))]
    private DateTime _createdAt;

    public string CreatedAtText => CreatedAt == DateTime.MinValue ? "—" : CreatedAt.ToString("dd.MM.yyyy HH:mm");

    public WordReplacementItemViewModel(string word, string replacement, DateTime createdAt = default)
    {
        _word = word;
        _replacement = replacement;
        _createdAt = createdAt;
    }

    public IBrush WordBackground => IsWordExactMatch ? MatchBackground : IsConflict ? ConflictBackground : Brushes.Transparent;
    public IBrush WordForeground => IsWordExactMatch ? MatchForeground : Brushes.Black;
    public IBrush WordBorderBrush => IsWordExactMatch ? MatchBorderBrush : IsConflict ? ConflictBorderBrush : Brushes.Transparent;
    public Thickness WordBorderThickness => (IsWordExactMatch || IsConflict) ? new Thickness(1) : new Thickness(0);
    public FontWeight WordFontWeight => IsWordExactMatch ? FontWeight.Bold : FontWeight.Normal;

    public IBrush ReplacementBackground => IsReplacementExactMatch ? MatchBackground : Brushes.Transparent;
    public IBrush ReplacementForeground => IsReplacementExactMatch ? MatchForeground : Brushes.Black;
    public IBrush ReplacementBorderBrush => IsReplacementExactMatch ? MatchBorderBrush : Brushes.Transparent;
    public Thickness ReplacementBorderThickness => IsReplacementExactMatch ? new Thickness(1) : new Thickness(0);
    public FontWeight ReplacementFontWeight => IsReplacementExactMatch ? FontWeight.Bold : FontWeight.Normal;
}
