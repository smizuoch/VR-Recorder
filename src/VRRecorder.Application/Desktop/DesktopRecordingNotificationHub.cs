using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingNotificationHub :
    ISavedRecordingSink,
    ICameraRestoreWarningSink,
    IDisposable
{
    private readonly object _deliveryGate = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscriber> _subscribers = [];
    private long _nextRevision;
    private long _nextSubscriptionId;
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

    private Task Publish(
        Func<long, DesktopRecordingNotification> createNotification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_deliveryGate)
        {
            DesktopRecordingNotification notification;
            Subscriber[] subscribers;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                notification = createNotification(checked(++_nextRevision));
                subscribers = [.. _subscribers.Values];
            }

            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber.Publish(notification);
                }
                catch (Exception)
                {
                    // Presentation observers cannot change recording or
                    // camera restoration outcomes.
                }
            }
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

    private sealed class Subscriber(
        Action<DesktopRecordingNotification> callback) : IDisposable
    {
        private readonly object _gate = new();
        private bool _disposed;

        public void Publish(DesktopRecordingNotification notification)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    callback(notification);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
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
