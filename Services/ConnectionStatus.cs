using System;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public abstract class ConnectionBase
{
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public event Action<ConnectionState>? StateChanged;

    protected void SetState(ConnectionState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
    }
}

public class ConnectionStatus : ConnectionStatusViewModel
{
    public ConnectionStatus(string name, ConnectionState state)
        : base(name, state)
    {
    }
}
