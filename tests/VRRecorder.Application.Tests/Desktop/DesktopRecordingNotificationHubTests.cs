using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingNotificationHubTests
{
    [Fact]
    public async Task SavedAndCameraWarningAreSeparateOrderedNotifications()
    {
        using var hub = new DesktopRecordingNotificationHub();
        List<DesktopRecordingNotification> notifications = [];
        using var broken = hub.Subscribe(_ => throw new InvalidOperationException(
            "A presentation subscriber must not fail recording finalization."));
        using var subscription = hub.Subscribe(notifications.Add);
        var saved = new FinalizedRecording(AbsolutePath("take.mp4"));
        var warning = new CameraRestoreWarning(
            CameraRestoreWarningReason.RecordingCompleted,
            new IOException("camera restore failed"));

        await ((ISavedRecordingSink)hub).PublishAsync(
            saved,
            CancellationToken.None);
        await ((ICameraRestoreWarningSink)hub).PublishAsync(
            warning,
            CancellationToken.None);

        var savedNotification = Assert.IsType<
            DesktopRecordingNotification.Saved>(notifications[0]);
        var warningNotification = Assert.IsType<
            DesktopRecordingNotification.CameraWarning>(notifications[1]);
        Assert.Equal(1, savedNotification.Revision);
        Assert.Same(saved, savedNotification.Recording);
        Assert.Equal(2, warningNotification.Revision);
        Assert.Same(warning, warningNotification.Warning);
    }

    [Fact]
    public async Task DisposedSubscriptionReceivesNoLaterNotification()
    {
        using var hub = new DesktopRecordingNotificationHub();
        var callCount = 0;
        var subscription = hub.Subscribe(_ => callCount++);
        subscription.Dispose();

        await ((ISavedRecordingSink)hub).PublishAsync(
            new FinalizedRecording(AbsolutePath("ignored.mp4")),
            CancellationToken.None);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task ReentrantPublicationPreservesOrderForEverySubscriber()
    {
        using var hub = new DesktopRecordingNotificationHub();
        List<long> firstRevisions = [];
        List<long> secondRevisions = [];
        var warning = new CameraRestoreWarning(
            CameraRestoreWarningReason.RecordingCompleted,
            new IOException("camera restore failed"));
        using var first = hub.Subscribe(notification =>
        {
            firstRevisions.Add(notification.Revision);
            if (notification is DesktopRecordingNotification.Saved)
            {
                ((ICameraRestoreWarningSink)hub)
                    .PublishAsync(warning, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        });
        using var second = hub.Subscribe(notification =>
            secondRevisions.Add(notification.Revision));

        await ((ISavedRecordingSink)hub).PublishAsync(
            new FinalizedRecording(AbsolutePath("ordered.mp4")),
            CancellationToken.None);

        Assert.Equal([1, 2], firstRevisions);
        Assert.Equal([1, 2], secondRevisions);
    }

    [Fact]
    public async Task PublicationAfterDisposalCannotInvalidateSavedRecording()
    {
        var hub = new DesktopRecordingNotificationHub();
        hub.Dispose();

        var failure = await Record.ExceptionAsync(() =>
            ((ISavedRecordingSink)hub).PublishAsync(
                new FinalizedRecording(AbsolutePath("already-saved.mp4")),
                CancellationToken.None));

        Assert.Null(failure);
    }

    [Fact]
    public void AudioLossAndActualRecoveryAreSeparateOrderedNotifications()
    {
        using var hub = new DesktopRecordingNotificationHub();
        List<DesktopRecordingNotification> notifications = [];
        using var subscription = hub.Subscribe(notifications.Add);
        var warning = new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Microphone,
            FramePosition: 4_800);
        var scheduled = new AudioSessionStatus(
            AudioSessionStatusKind.EndpointRediscoveryScheduled,
            AudioInput.Desktop,
            FramePosition: 5_000,
            RediscoveryBudget: TimeSpan.FromSeconds(5));
        var recovered = new AudioSessionStatus(
            AudioSessionStatusKind.InputRecovered,
            AudioInput.Microphone,
            FramePosition: 9_600);

        ((IAudioSessionEventSink)hub).Publish(warning);
        ((IAudioSessionEventSink)hub).Publish(scheduled);
        ((IAudioSessionEventSink)hub).Publish(recovered);

        var audioWarning = Assert.IsType<
            DesktopRecordingNotification.AudioWarning>(notifications[0]);
        Assert.Equal(1, audioWarning.Revision);
        Assert.Same(warning, audioWarning.Warning);
        var audioRecovered = Assert.IsType<
            DesktopRecordingNotification.AudioRecovered>(notifications[1]);
        Assert.Equal(2, audioRecovered.Revision);
        Assert.Same(recovered, audioRecovered.Recovery);
        Assert.Equal(2, notifications.Count);
    }

    [Fact]
    public async Task RejectsMissingInputsCanceledPublishAndLateSubscriptions()
    {
        var hub = new DesktopRecordingNotificationHub();
        Assert.Throws<ArgumentNullException>(() => hub.Subscribe(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ISavedRecordingSink)hub).PublishAsync(
                null!,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ICameraRestoreWarningSink)hub).PublishAsync(
                null!,
                CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() =>
            ((IAudioSessionEventSink)hub).Publish((AudioSessionWarning)null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((IAudioSessionEventSink)hub).Publish((AudioSessionStatus)null!));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((ISavedRecordingSink)hub).PublishAsync(
                new FinalizedRecording(AbsolutePath("canceled.mp4")),
                cancellation.Token));

        hub.Dispose();
        hub.Dispose();
        Assert.Throws<ObjectDisposedException>(() => hub.Subscribe(_ => { }));
    }

    [Fact]
    public async Task DisposingSubscriptionWaitsForForeignCallback()
    {
        using var hub = new DesktopRecordingNotificationHub();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = hub.Subscribe(_ =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        var publish = Task.Run(() =>
            ((ISavedRecordingSink)hub).PublishAsync(
                new FinalizedRecording(AbsolutePath("blocked.mp4")),
                CancellationToken.None));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = Task.Run(subscription.Dispose);
        await Task.Yield();
        Assert.False(disposal.IsCompleted);
        release.TrySetResult();

        await Task.WhenAll(publish, disposal).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CallbackCanReenterDisposeWhileForeignDisposeWaits()
    {
        var hub = new DesktopRecordingNotificationHub();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReentrantDispose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = hub.Subscribe(_ =>
        {
            entered.TrySetResult();
            allowReentrantDispose.Task.GetAwaiter().GetResult();
            hub.Dispose();
        });
        var publish = Task.Run(() =>
            ((ISavedRecordingSink)hub).PublishAsync(
                new FinalizedRecording(AbsolutePath("reentrant.mp4")),
                CancellationToken.None));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = Task.Run(hub.Dispose);

        var observedDisposing = false;
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            try
            {
                hub.Subscribe(_ => { }).Dispose();
                await Task.Yield();
            }
            catch (ObjectDisposedException)
            {
                observedDisposing = true;
                break;
            }
        }
        Assert.True(observedDisposing);
        Assert.False(disposal.IsCompleted);
        allowReentrantDispose.TrySetResult();

        await Task.WhenAll(publish, disposal).WaitAsync(TimeSpan.FromSeconds(5));
        hub.Dispose();
    }

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        "vr-recorder-notification-tests",
        name);
}
