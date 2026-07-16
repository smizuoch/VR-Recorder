using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Recording;

namespace VRRecorder.App;

internal sealed class DesktopTestRecordingPlaybackRuntime
    : ITestRecordingPlaybackRuntime
{
    private readonly object _gate = new();
    private readonly DesktopRecordingCommandHost _recording;
    private readonly DesktopRecordingNotificationHub _notifications;
    private Task? _startOperation;

    public DesktopTestRecordingPlaybackRuntime(
        DesktopRecordingCommandHost recording,
        DesktopRecordingNotificationHub notifications)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(notifications);
        _recording = recording;
        _notifications = notifications;
    }

    public RecorderStatusSnapshot Current => _recording.Current;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Current.State != RecorderState.Ready)
        {
            throw new InvalidOperationException(
                $"A first-run test recording cannot start from {Current.State}.");
        }

        Task operation;
        lock (_gate)
        {
            operation = _recording.ToggleAsync(CancellationToken.None);
            _startOperation = operation;
        }
        return WaitForCallerAsync(operation, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? startOperation;
        lock (_gate)
        {
            startOperation = _startOperation;
        }
        if (startOperation is not null)
        {
            await WaitForCallerAsync(startOperation, cancellationToken)
                .ConfigureAwait(false);
        }

        var stopOperation = Current.State switch
        {
            RecorderState.Ready or
            RecorderState.NoSignal or
            RecorderState.Faulted or
            RecorderState.ComplianceFault =>
                Task.CompletedTask,
            RecorderState.Arming or
            RecorderState.Countdown or
            RecorderState.Recording or
            RecorderState.SignalLost or
            RecorderState.Stopping =>
                _recording.ToggleAsync(CancellationToken.None),
            _ => throw new InvalidOperationException(
                $"A first-run test recording cannot stop from {Current.State}."),
        };
        await WaitForCallerAsync(stopOperation, cancellationToken)
            .ConfigureAwait(false);
    }

    public IDisposable SubscribeSaved(
        Action<FinalizedRecording> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        return _notifications.Subscribe(notification =>
        {
            if (notification is DesktopRecordingNotification.Saved saved)
            {
                subscriber(saved.Recording);
            }
        });
    }

    private static Task WaitForCallerAsync(
        Task operation,
        CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? operation.WaitAsync(cancellationToken)
            : operation;
}
