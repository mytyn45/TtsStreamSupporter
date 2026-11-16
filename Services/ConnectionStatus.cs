using System;
using System.Threading;
using System.Threading.Tasks;
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

    public virtual Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        return Task.FromResult(State == ConnectionState.Connected);
    }
}

public class ConnectionStatus : ConnectionStatusViewModel
{
    public ConnectionStatus(string name, ConnectionState state)
        : base(name, state)
    {
    }
}
