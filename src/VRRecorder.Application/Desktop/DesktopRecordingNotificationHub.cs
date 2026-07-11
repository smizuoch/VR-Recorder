using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingNotificationHub :
    ISavedRecordingSink,
    ICameraRestoreWarningSink,
    IAudioSessionEventSink,
    IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscriber> _subscribers = [];
    private Subscriber[] _disposalSubscribers = [];
    private long _nextRevision;
    private long _nextSubscriptionId;
    private bool _disposeCompleted;
    private bool _disposed;

    public IDisposable Subscribe(
        Action<DesktopRecordingNotification> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = checked(++_nextSubscriptionId);
            _subscribers.Add(id, new Subscriber(subscriber));
            return new Subscription(this, id);
        }
    }

    Task ISavedRecordingSink.PublishAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return Publish(
            revision => new DesktopRecordingNotification.Saved(
                revision,
                recording),
            cancellationToken);
    }

    Task ICameraRestoreWarningSink.PublishAsync(
        CameraRestoreWarning warning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return Publish(
            revision => new DesktopRecordingNotification.CameraWarning(
                revision,
                warning),
            cancellationToken);
    }

    void IAudioSessionEventSink.Publish(AudioSessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        _ = Publish(
            revision => new DesktopRecordingNotification.AudioWarning(
                revision,
                warning),
            CancellationToken.None);
    }

    void IAudioSessionEventSink.Publish(AudioSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Kind != AudioSessionStatusKind.InputRecovered)
        {
            return;
        }

        _ = Publish(
            revision => new DesktopRecordingNotification.AudioRecovered(
                revision,
                status),
            CancellationToken.None);
    }

    public void Dispose()
    {
        Subscriber[] subscribers;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                subscribers = [.. _subscribers.Values];
                _disposalSubscribers = subscribers;
                _subscribers.Clear();
            }
            else
            {
                if (IsSubscriberCallbackThread(
                        _disposalSubscribers,
                        Environment.CurrentManagedThreadId))
                {
                    return;
                }

                while (!_disposeCompleted)
                {
                    Monitor.Wait(_gate);
                }

                return;
            }
        }

        try
        {
            foreach (var subscriber in subscribers)
            {
                subscriber.Dispose();
            }
        }
        finally
        {
            lock (_gate)
            {
                _disposeCompleted = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private Task Publish(
        Func<long, DesktopRecordingNotification> createNotification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DesktopRecordingNotification notification;
        Subscriber[] subscribers;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            notification = createNotification(checked(++_nextRevision));
            subscribers = [.. _subscribers.Values];
            foreach (var subscriber in subscribers)
            {
                subscriber.Enqueue(notification);
            }
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Drain();
        }

        return Task.CompletedTask;
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

    private static bool IsSubscriberCallbackThread(
        IEnumerable<Subscriber> subscribers,
        int threadId) =>
        subscribers.Any(subscriber => subscriber.IsCallbackThread(threadId));

    private sealed class Subscriber(
        Action<DesktopRecordingNotification> callback) : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<DesktopRecordingNotification> _pending = [];
        private long _lastQueuedRevision;
        private bool _callbackActive;
        private int _callbackThreadId;
        private bool _draining;
        private bool _disposed;

        public void Enqueue(DesktopRecordingNotification notification)
        {
            lock (_gate)
            {
                if (_disposed ||
                    notification.Revision <= _lastQueuedRevision)
                {
                    return;
                }

                _lastQueuedRevision = notification.Revision;
                _pending.Enqueue(notification);
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

            while (TryBeginCallback(out var notification))
            {
                try
                {
                    callback(notification);
                }
                catch (Exception)
                {
                    // Presentation observers cannot change recording or
                    // camera restoration outcomes.
                }
                finally
                {
                    CompleteCallback();
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

        public bool IsCallbackThread(int threadId)
        {
            lock (_gate)
            {
                return _callbackActive && _callbackThreadId == threadId;
            }
        }

        private bool TryBeginCallback(
            out DesktopRecordingNotification notification)
        {
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0)
                {
                    _draining = false;
                    Monitor.PulseAll(_gate);
                    notification = null!;
                    return false;
                }

                notification = _pending.Dequeue();
                _callbackActive = true;
                _callbackThreadId = Environment.CurrentManagedThreadId;
                return true;
            }
        }

        private void CompleteCallback()
        {
            lock (_gate)
            {
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
        DesktopRecordingNotificationHub owner,
        long id) : IDisposable
    {
        private DesktopRecordingNotificationHub? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(id);
        }
    }
}
