using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TtsStreamSupporter.Services;

public class MessageQueue : IDisposable
{
    private sealed class QueueItem
    {
        public required Task<PreparedAudio> AudioTask { get; init; }

        public int SkipRequested;
    }

    private readonly ConcurrentQueue<QueueItem> _queue = new();

    private readonly SpeechPlayerService _player;

    private readonly SemaphoreSlim _queueSignal =
        new(0);

    private readonly object _lock = new();

    private QueueItem? _current;

    private bool _disposed;

    public MessageQueue(
        SpeechPlayerService player)
    {
        _player = player;

        // Запускаем постоянный обработчик очереди.
        _ = ProcessQueueAsync();
    }
    public void Enqueue(
        ProcessedTtsMessage message)
    {
        if (_disposed)
            return;

        try
        {
            var audioTask =
                _player.GenerateAsync(message);

            var item = new QueueItem
            {
                AudioTask = audioTask
            };

            _queue.Enqueue(item);

            Log.Debug(
                "Сообщение добавлено в очередь. " +
                "Размер очереди: {Count}",
                _queue.Count);

            _queueSignal.Release();
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Не удалось добавить сообщение в очередь");
        }
    }
    private async Task ProcessQueueAsync()
    {
        while (!_disposed)
        {
            try
            {
                await _queueSignal.WaitAsync();

                if (_disposed)
                    break;

                if (!_queue.TryDequeue(
                    out var item))
                {
                    continue;
                }

                lock (_lock)
                {
                    _current = item;
                }

                try
                {
                    PreparedAudio audio;

                    try
                    {
                        audio =
                            await item.AudioTask;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            ex,
                            "Не удалось сгенерировать TTS");

                        continue;
                    }
                    if (Volatile.Read(
                            ref item.SkipRequested) != 0)
                    {
                        Log.Debug(
                            "Сгенерированное сообщение " +
                            "было пропущено");

                        audio.Dispose();

                        continue;
                    }

                    try
                    {
                        Log.Debug(
                            "Начинается воспроизведение");

                        await _player.PlayAsync(audio);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            ex,
                            "Ошибка воспроизведения TTS");
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(
                            _current,
                            item))
                        {
                            _current = null;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Ошибка обработчика очереди");
            }
        }
    }
    public void SkipCurrent()
    {
        QueueItem? current;

        lock (_lock)
        {
            current = _current;

            if (current == null)
            {
                Log.Debug(
                    "SkipCurrent: текущего сообщения нет");

                return;
            }
            Interlocked.Exchange(
                ref current.SkipRequested,
                1);
        }

        Log.Debug(
            "Пропускаем текущее сообщение");

        _player.Stop();
    }
    public void SkipNext()
    {
        if (!_queue.TryDequeue(
            out var item))
        {
            Log.Debug(
                "SkipNext: следующего сообщения нет");

            return;
        }

        Interlocked.Exchange(
            ref item.SkipRequested,
            1);
        _ = DisposeWhenReadyAsync(item);

        Log.Debug(
            "Следующее сообщение пропущено");
    }
    public void Clear()
    {
        while (_queue.TryDequeue(
            out var item))
        {
            Interlocked.Exchange(
                ref item.SkipRequested,
                1);

            _ = DisposeWhenReadyAsync(item);
        }

        Log.Debug(
            "Очередь очищена");
    }
    private async Task DisposeWhenReadyAsync(
        QueueItem item)
    {
        try
        {
            var audio =
                await item.AudioTask;

            audio.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Генерация пропущенного сообщения " +
                "завершилась ошибкой");
        }
    }

    public int Count =>
        _queue.Count;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _player.Stop();

        Clear();

        // Разбудить ProcessQueueAsync.
        try
        {
            _queueSignal.Release();
        }
        catch
        {
        }

        _queueSignal.Dispose();
    }
}