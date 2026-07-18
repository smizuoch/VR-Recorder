using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class RecordingNotificationFanOutTests
{
    [Fact]
    public void CompositeSinksRejectEachNullDependency()
    {
        List<string> calls = [];
        var saved = new SavedSink("saved", calls);
        var warning = new WarningSink("warning", calls);
        var audio = new AudioSink("audio", calls);

        Assert.Throws<ArgumentNullException>(() =>
            new CompositeSavedRecordingSink(null!, saved));
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeSavedRecordingSink(saved, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeCameraRestoreWarningSink(null!, warning));
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeCameraRestoreWarningSink(warning, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeAudioSessionEventSink(null!, audio));
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeAudioSessionEventSink(audio, null!));
    }

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

    [Fact]
    public async Task AsyncFanOutRejectsNullAndPreCanceledInputBeforeChildren()
    {
        List<string> calls = [];
        var saved = new CompositeSavedRecordingSink(
            new SavedSink("first-saved", calls),
            new SavedSink("second-saved", calls));
        var warnings = new CompositeCameraRestoreWarningSink(
            new WarningSink("first-warning", calls),
            new WarningSink("second-warning", calls));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            saved.PublishAsync(null!, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            saved.PublishAsync(
                new FinalizedRecording(AbsolutePath("canceled.mp4")),
                cancellation.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            warnings.PublishAsync(null!, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            warnings.PublishAsync(
                new CameraRestoreWarning(
                    CameraRestoreWarningReason.RecordingCompleted,
                    new IOException("camera restore failed")),
                cancellation.Token));

        Assert.Empty(calls);
    }

    [Fact]
    public void AudioEventsArePublishedToBothSinksInOrder()
    {
        List<string> calls = [];
        var first = new AudioSink("diagnostics", calls);
        var second = new AudioSink("presentation", calls);
        var sink = new CompositeAudioSessionEventSink(first, second);
        var warning = new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Desktop,
            FramePosition: 4_800);
        var status = new AudioSessionStatus(
            AudioSessionStatusKind.InputRecovered,
            AudioInput.Desktop,
            FramePosition: 9_600);

        sink.Publish(warning);
        sink.Publish(status);

        Assert.Equal(
            [
                "diagnostics-warning",
                "presentation-warning",
                "diagnostics-status",
                "presentation-status",
            ],
            calls);
        Assert.Same(warning, first.Warning);
        Assert.Same(warning, second.Warning);
        Assert.Same(status, first.Status);
        Assert.Same(status, second.Status);
    }

    [Fact]
    public void AudioFanOutRejectsNullInputBeforeChildren()
    {
        List<string> calls = [];
        var sink = new CompositeAudioSessionEventSink(
            new AudioSink("first", calls),
            new AudioSink("second", calls));

        Assert.Throws<ArgumentNullException>(() => sink.Publish(
            (AudioSessionWarning)null!));
        Assert.Throws<ArgumentNullException>(() => sink.Publish(
            (AudioSessionStatus)null!));

        Assert.Empty(calls);
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
            Warning = warning;
            calls.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class AudioSink(
        string name,
        List<string> calls) : IAudioSessionEventSink
    {
        public AudioSessionWarning? Warning { get; private set; }

        public AudioSessionStatus? Status { get; private set; }

        public void Publish(AudioSessionWarning warning)
        {
            Warning = warning;
            calls.Add($"{name}-warning");
        }

        public void Publish(AudioSessionStatus status)
        {
            Status = status;
            calls.Add($"{name}-status");
        }
    }
}
