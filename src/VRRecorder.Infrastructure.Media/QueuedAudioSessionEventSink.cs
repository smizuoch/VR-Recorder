using System.Threading.Channels;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Media;

public sealed class QueuedAudioSessionEventSink
    : IAudioSessionEventSink, IDisposable
{
    public const int DefaultCapacity = 64;
    private readonly Channel<AudioEvent> _events;
    private readonly IAudioSessionEventSink _sink;
    private readonly Task _worker;
    private int _deliveryThreadId;
    private int _disposed;

    public QueuedAudioSessionEventSink(
        IAudioSessionEventSink sink,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _sink = sink;
        _events = Channel.CreateBounded<AudioEvent>(
            new BoundedChannelOptions(capacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
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
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _events.Writer.TryComplete();
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
        if (Volatile.Read(ref _disposed) == 0)
        {
            _events.Writer.TryWrite(audioEvent);
        }
    }

    private async Task DeliverAsync()
    {
        await foreach (var audioEvent in _events.Reader.ReadAllAsync())
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
    }

    private abstract record AudioEvent
    {
        public sealed record Warning(AudioSessionWarning Value) : AudioEvent;

        public sealed record Status(AudioSessionStatus Value) : AudioEvent;
    }
}
