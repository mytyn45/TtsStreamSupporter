using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter.Models;

public class AppWarning : AppWarningViewModel
{
    public AppWarning(string message, string backgroundHex = "#E65100")
        : base(message, backgroundHex)
    {
    }
}
