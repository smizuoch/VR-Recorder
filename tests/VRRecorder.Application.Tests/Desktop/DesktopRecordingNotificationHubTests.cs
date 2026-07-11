using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

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

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        "vr-recorder-notification-tests",
        name);
}
