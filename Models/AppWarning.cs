using Avalonia.Media;

namespace TtsStreamSupporter.Models;

public class AppWarning
{
    public string Message { get; }
    public IBrush BackgroundBrush { get; }
    public IBrush ForegroundBrush { get; }

    public AppWarning(string message, string backgroundHex = "#E65100")
    {
        Message = message;
        BackgroundBrush = Brush.Parse(backgroundHex);
        ForegroundBrush = Brushes.White;
    }
}

