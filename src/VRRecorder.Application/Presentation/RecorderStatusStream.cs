namespace VRRecorder.Application.Presentation;

public sealed class RecorderStatusHub : IRecorderStatusSource, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Action<RecorderStatusSnapshot>>
        _subscribers = [];
    private RecorderStatusSnapshot _current;
    private long _nextSubscriptionId;
    private bool _disposed;

    public RecorderStatusHub(RecorderStatusSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    public RecorderStatusSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public IDisposable Subscribe(Action<RecorderStatusSnapshot> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        long id;
        RecorderStatusSnapshot current;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            id = ++_nextSubscriptionId;
            _subscribers.Add(id, subscriber);
            current = _current;
        }

        PublishBestEffort(id, subscriber, current);
        return new Subscription(this, id);
    }

    public bool TryPublish(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        KeyValuePair<long, Action<RecorderStatusSnapshot>>[] subscribers;
        lock (_gate)
        {
            if (_disposed || status.Revision <= _current.Revision)
            {
                return false;
            }

            _current = status;
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            PublishBestEffort(subscriber.Key, subscriber.Value, status);
        }

        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscribers.Clear();
        }
    }

    private void PublishBestEffort(
        long id,
        Action<RecorderStatusSnapshot> subscriber,
        RecorderStatusSnapshot status)
    {
        lock (_gate)
        {
            if (_disposed ||
                !_subscribers.TryGetValue(id, out var current) ||
                !ReferenceEquals(current, subscriber))
            {
                return;
            }
        }

        try
        {
            subscriber(status);
        }
        catch (Exception)
        {
            // Status observers are presentation concerns and must never alter
            // recording, finalization, or compliance outcomes.
        }
    }

    private void Unsubscribe(long id)
    {
        lock (_gate)
        {
            _subscribers.Remove(id);
        }
    }

    private sealed class Subscription(
        RecorderStatusHub owner,
        long id) : IDisposable
    {
        private RecorderStatusHub? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(id);
        }
    }
}
