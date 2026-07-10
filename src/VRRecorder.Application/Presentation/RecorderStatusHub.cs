namespace VRRecorder.Application.Presentation;

public sealed class RecorderStatusHub : IRecorderStatusSource, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscriber> _subscribers = [];
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
        Subscriber registration;
        long initialRevision;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            id = ++_nextSubscriptionId;
            registration = new Subscriber(subscriber);
            registration.Enqueue(_current);
            initialRevision = _current.Revision;
            _subscribers.Add(id, registration);
        }

        registration.DrainAndWaitFor(initialRevision);
        return new Subscription(this, id);
    }

    public bool TryPublish(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Subscriber[] subscribers;
        lock (_gate)
        {
            if (_disposed || status.Revision <= _current.Revision)
            {
                return false;
            }

            _current = status;
            subscribers = [.. _subscribers.Values];
            foreach (var subscriber in subscribers)
            {
                subscriber.Enqueue(status);
            }
        }

        // Delivery is queued synchronously in revision order. If another
        // callback is already draining a subscriber, this publisher does not
        // wait for that presentation callback to finish.
        foreach (var subscriber in subscribers)
        {
            subscriber.Drain();
        }

        return true;
    }

    public void Dispose()
    {
        Subscriber[] subscribers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscribers = [.. _subscribers.Values];
            _subscribers.Clear();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Dispose();
        }
    }

    private void Unsubscribe(long id)
    {
        Subscriber? subscriber;
        lock (_gate)
        {
            _subscribers.Remove(id, out subscriber);
        }

        subscriber?.Dispose();
    }

    private sealed class Subscriber(
        Action<RecorderStatusSnapshot> callback) : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<RecorderStatusSnapshot> _pending = [];
        private long _lastQueuedRevision = -1;
        private long _lastCompletedRevision = -1;
        private bool _draining;
        private bool _callbackActive;
        private int _callbackThreadId;
        private bool _disposed;

        public void Enqueue(RecorderStatusSnapshot status)
        {
            lock (_gate)
            {
                if (_disposed || status.Revision <= _lastQueuedRevision)
                {
                    return;
                }

                _lastQueuedRevision = status.Revision;
                _pending.Enqueue(status);
            }
        }

        public void DrainAndWaitFor(long revision)
        {
            Drain();
            lock (_gate)
            {
                while (!_disposed && _lastCompletedRevision < revision)
                {
                    Monitor.Wait(_gate);
                }
            }
        }

        public void Drain()
        {
            lock (_gate)
            {
                if (_disposed || _draining)
                {
                    return;
                }

                _draining = true;
            }

            while (TryBeginCallback(out var status))
            {
                try
                {
                    callback(status);
                }
                catch (Exception)
                {
                    // Status observers are presentation concerns and must
                    // never alter recording or compliance outcomes.
                }
                finally
                {
                    CompleteCallback(status.Revision);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    WaitForForeignCallback();
                    return;
                }

                _disposed = true;
                _pending.Clear();
                Monitor.PulseAll(_gate);
                WaitForForeignCallback();
            }
        }

        private bool TryBeginCallback(
            out RecorderStatusSnapshot status)
        {
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0)
                {
                    _draining = false;
                    Monitor.PulseAll(_gate);
                    status = null!;
                    return false;
                }

                status = _pending.Dequeue();
                _callbackActive = true;
                _callbackThreadId = Environment.CurrentManagedThreadId;
                return true;
            }
        }

        private void CompleteCallback(long revision)
        {
            lock (_gate)
            {
                _lastCompletedRevision = Math.Max(
                    _lastCompletedRevision,
                    revision);
                _callbackActive = false;
                _callbackThreadId = 0;
                Monitor.PulseAll(_gate);
            }
        }

        private void WaitForForeignCallback()
        {
            var currentThread = Environment.CurrentManagedThreadId;
            while (_callbackActive && _callbackThreadId != currentThread)
            {
                Monitor.Wait(_gate);
            }
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
