using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;

namespace TtsStreamSupporter.ViewModels;

public class WordReplacementItemViewModel : INotifyPropertyChanged
{
    private static readonly IBrush MatchBackground = Brush.Parse("#BBDEFB");
    private static readonly IBrush MatchBorderBrush = Brush.Parse("#1976D2");
    private static readonly IBrush MatchForeground = Brush.Parse("#0D47A1");

    private string _word;
    private string _replacement;
    private bool _isWordExactMatch;
    private bool _isReplacementExactMatch;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WordReplacementItemViewModel(string word, string replacement)
    {
        _word = word;
        _replacement = replacement;
    }

    public string Word
    {
        get => _word;
        set
        {
            if (_word == value) return;
            _word = value;
            OnPropertyChanged();
        }
    }

    public string Replacement
    {
        get => _replacement;
        set
        {
            if (_replacement == value) return;
            _replacement = value;
            OnPropertyChanged();
        }
    }

    public bool IsWordExactMatch
    {
        get => _isWordExactMatch;
        set
        {
            if (_isWordExactMatch == value) return;
            _isWordExactMatch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WordBackground));
            OnPropertyChanged(nameof(WordForeground));
            OnPropertyChanged(nameof(WordBorderBrush));
            OnPropertyChanged(nameof(WordBorderThickness));
            OnPropertyChanged(nameof(WordFontWeight));
        }
    }

    public bool IsReplacementExactMatch
    {
        get => _isReplacementExactMatch;
        set
        {
            if (_isReplacementExactMatch == value) return;
            _isReplacementExactMatch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReplacementBackground));
            OnPropertyChanged(nameof(ReplacementForeground));
            OnPropertyChanged(nameof(ReplacementBorderBrush));
            OnPropertyChanged(nameof(ReplacementBorderThickness));
            OnPropertyChanged(nameof(ReplacementFontWeight));
        }
    }

    public IBrush WordBackground => IsWordExactMatch ? MatchBackground : Brushes.Transparent;
    public IBrush WordForeground => IsWordExactMatch ? MatchForeground : Brushes.Black;
    public IBrush WordBorderBrush => IsWordExactMatch ? MatchBorderBrush : Brushes.Transparent;
    public Thickness WordBorderThickness => IsWordExactMatch ? new Thickness(1) : new Thickness(0);
    public FontWeight WordFontWeight => IsWordExactMatch ? FontWeight.Bold : FontWeight.Normal;

    public IBrush ReplacementBackground => IsReplacementExactMatch ? MatchBackground : Brushes.Transparent;
    public IBrush ReplacementForeground => IsReplacementExactMatch ? MatchForeground : Brushes.Black;
    public IBrush ReplacementBorderBrush => IsReplacementExactMatch ? MatchBorderBrush : Brushes.Transparent;
    public Thickness ReplacementBorderThickness => IsReplacementExactMatch ? new Thickness(1) : new Thickness(0);
    public FontWeight ReplacementFontWeight => IsReplacementExactMatch ? FontWeight.Bold : FontWeight.Normal;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

