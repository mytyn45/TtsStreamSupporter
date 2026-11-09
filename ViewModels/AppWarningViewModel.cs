using Avalonia.Media;

namespace TtsStreamSupporter.ViewModels;

public class AppWarningViewModel : ViewModelBase
{
    public string Message { get; }
    public IBrush BackgroundBrush { get; }
    public IBrush ForegroundBrush { get; }

    public AppWarningViewModel(string message, string backgroundHex = "#E65100")
    {
        Message = message;
        BackgroundBrush = Brush.Parse(backgroundHex);
        ForegroundBrush = Brushes.White;
    }
}
