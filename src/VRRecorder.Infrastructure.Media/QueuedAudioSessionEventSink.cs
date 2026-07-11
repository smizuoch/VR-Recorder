using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

public sealed class QueuedAudioSessionEventSink
    : IAudioSessionEventSink, IDisposable
{
    public const int DefaultCapacity = 64;
    private const int MinimumCapacity = 2;
    private readonly SemaphoreSlim _available = new(0);
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly LinkedList<AudioEvent> _pending = [];
    private readonly IAudioSessionEventSink _sink;
    private readonly Task _worker;
    private int _deliveryThreadId;
    private bool _disposed;

    public QueuedAudioSessionEventSink(
        IAudioSessionEventSink sink,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (capacity < MinimumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "The queue must retain the latest state of both audio inputs.");
        }

        _sink = sink;
        _capacity = capacity;
        _worker = Task.Run(DeliverAsync);
    }

    public void Publish(AudioSessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        Enqueue(new AudioEvent.Warning(warning));
    }

    public void Publish(AudioSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Enqueue(new AudioEvent.Status(status));
    }

    public void Dispose()
    {
        var wakeWorker = false;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                wakeWorker = true;
            }
        }

        if (wakeWorker)
        {
            _available.Release();
        }

        if (Volatile.Read(ref _deliveryThreadId) !=
            Environment.CurrentManagedThreadId)
        {
            _worker.GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    private void Enqueue(AudioEvent audioEvent)
    {
        var wakeWorker = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Count == _capacity)
            {
                _pending.Remove(
                    FindOldestForInput(audioEvent.Input) ?? _pending.First!);
            }

            wakeWorker = _pending.Count == 0;
            _pending.AddLast(audioEvent);
        }

        if (wakeWorker)
        {
            _available.Release();
        }
    }

    private async Task DeliverAsync()
    {
        while (true)
        {
            await _available.WaitAsync().ConfigureAwait(false);
            while (TryTake(out var audioEvent))
            {
                try
                {
                    Volatile.Write(
                        ref _deliveryThreadId,
                        Environment.CurrentManagedThreadId);
                    switch (audioEvent)
                    {
                        case AudioEvent.Warning warning:
                            _sink.Publish(warning.Value);
                            break;
                        case AudioEvent.Status status:
                            _sink.Publish(status.Value);
                            break;
                    }
                }
                catch (Exception)
                {
                    // Observer failures cannot interrupt later audio events.
                }
                finally
                {
                    Volatile.Write(ref _deliveryThreadId, 0);
                }
            }

            lock (_gate)
            {
                if (_disposed && _pending.Count == 0)
                {
                    return;
                }
            }
        }
    }

    private LinkedListNode<AudioEvent>? FindOldestForInput(AudioInput input)
    {
        for (var node = _pending.First; node is not null; node = node.Next)
        {
            if (node.Value.Input == input)
            {
                return node;
            }
        }

        return null;
    }

    private bool TryTake(out AudioEvent audioEvent)
    {
        lock (_gate)
        {
            if (_pending.First is null)
            {
                audioEvent = null!;
                return false;
            }

            audioEvent = _pending.First.Value;
            _pending.RemoveFirst();
            return true;
        }
    }

    private abstract record AudioEvent(AudioInput Input)
    {
        public sealed record Warning(AudioSessionWarning Value) :
            AudioEvent(Value.Input);

        public sealed record Status(AudioSessionStatus Value) :
            AudioEvent(Value.Input);
    }
}
