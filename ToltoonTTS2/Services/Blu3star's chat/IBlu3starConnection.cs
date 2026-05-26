namespace ToltoonTTS2.Services.Blu3star_s_chat
{
    public interface IBlu3starConnection
    {
        Task Blue3starConnect();
        event EventHandler<Blu3starMessageEventArgs> MessageReceived;
        //event EventHandler<TwitchConnectionState> ConnectionStateChanged;
        //TwitchConnectionState CurrentState { get; }
    }
}
