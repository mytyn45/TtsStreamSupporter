using System;
using System.Threading;
using System.Threading.Tasks;

namespace TtsStreamSupporter.Services;

/// <summary>
/// Гарантирует минимальный интервал между последовательными вызовами.
/// В отличие от Thread.Sleep на входе, ставит вызовы в очередь FIFO
/// и не блокирует поток на время ожидания.
/// </summary>
public class AsyncRateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastCallUtc = DateTime.MinValue;

    public async Task WaitTurnAsync(
        TimeSpan minInterval,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (minInterval <= TimeSpan.Zero)
                return;

            var elapsed = DateTime.UtcNow - _lastCallUtc;
            var remaining = minInterval - elapsed;

            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct).ConfigureAwait(false);

            _lastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}