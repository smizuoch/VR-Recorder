using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class RecordingNotificationFanOutTests
{
    [Fact]
    public async Task SavedRecordingIsPublishedToBothSinksInOrder()
    {
        List<string> calls = [];
        var first = new SavedSink("diagnostics", calls);
        var second = new SavedSink("presentation", calls);
        var recording = new FinalizedRecording(AbsolutePath("take.mp4"));
        var sink = new CompositeSavedRecordingSink(first, second);

        await sink.PublishAsync(recording, CancellationToken.None);

        Assert.Equal(["diagnostics", "presentation"], calls);
        Assert.Same(recording, first.Recording);
        Assert.Same(recording, second.Recording);
    }

    [Fact]
    public async Task CameraWarningIsPublishedToBothSinksInOrder()
    {
        List<string> calls = [];
        var first = new WarningSink("diagnostics", calls);
        var second = new WarningSink("presentation", calls);
        var warning = new CameraRestoreWarning(
            CameraRestoreWarningReason.RecordingCompleted,
            new IOException("camera restore failed"));
        var sink = new CompositeCameraRestoreWarningSink(first, second);

        await sink.PublishAsync(warning, CancellationToken.None);

        Assert.Equal(["diagnostics", "presentation"], calls);
        Assert.Same(warning, first.Warning);
        Assert.Same(warning, second.Warning);
    }

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        "vr-recorder-notification-fan-out-tests",
        name);

    private sealed class SavedSink(
        string name,
        List<string> calls) : ISavedRecordingSink
    {
        public FinalizedRecording? Recording { get; private set; }

        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recording = recording;
            calls.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class WarningSink(
        string name,
        List<string> calls) : ICameraRestoreWarningSink
    {
        public CameraRestoreWarning? Warning { get; private set; }

        public Task PublishAsync(
            CameraRestoreWarning warning,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Warning = warning;
            calls.Add(name);
            return Task.CompletedTask;
        }
    }
}
