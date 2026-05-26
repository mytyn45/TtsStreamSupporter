using ToltoonTTS2.Services.VK;
using static Blu3starConnection;

namespace ToltoonTTS2.Services.Blu3star_s_chat
{
    public interface IBlu3starConnection
    {
        Task Blue3starConnect(string Api);
        event EventHandler<Blu3starMessageEventArgs> MessageReceived;
        
        event EventHandler<Blu3starConnectionState> ConnectionStateChanged;
        Blu3starConnectionState CurrentState { get; }
    }
}
