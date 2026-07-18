using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class ProductionWristTelemetrySourceTests
{
    [Fact]
    public void ProjectsLiveProfileSignalAudioPlacementAndElapsedTime()
    {
        var clock = new ControllableClock();
        var source = new ProductionWristTelemetrySource(clock);
        ((IRecordingMediaEventSink)source).Publish(new RecordingMediaProfile(
            1_920,
            1_080,
            VideoPixelFormat.Bgra8,
            59.94,
            1_280,
            720,
            60,
            EncoderKind.Nvenc,
            GpuVendor.Nvidia));
        var recording = RecorderStatusSnapshot.Create(
            1,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Mixed));

        var initial = Assert.IsType<WristTelemetrySnapshot>(source.Capture(
            recording,
            OverlayPlacementMode.WristDock,
            EnglishUiLocalizer.Instance));
        clock.Advance(TimeSpan.FromSeconds(2));
        ((IAudioSessionEventSink)source).Publish(new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Desktop,
            48_000));
        var signalLost = Assert.IsType<WristTelemetrySnapshot>(source.Capture(
            RecorderStatusSnapshot.Create(
                2,
                RecorderState.SignalLost,
                recording.AudioControlState),
            OverlayPlacementMode.WorldPin,
            EnglishUiLocalizer.Instance));

        Assert.Equal(TimeSpan.Zero, initial.ElapsedRecordingTime);
        Assert.Equal(TimeSpan.FromSeconds(2), signalLost.ElapsedRecordingTime);
        Assert.Equal(1_280, signalLost.CanvasWidth);
        Assert.Equal(720, signalLost.CanvasHeight);
        Assert.Equal(60, signalLost.TargetFramesPerSecond);
        Assert.Equal(59.94, signalLost.ActualFramesPerSecond);
        Assert.Equal(WristSignalHealth.Unavailable, signalLost.SpoutSignal);
        Assert.Equal(
            WristSignalHealth.Unavailable,
            signalLost.DesktopAudioSignal);
        Assert.Equal(
            WristSignalHealth.Available,
            signalLost.MicrophoneSignal);
        Assert.Equal("NVENC", signalLost.EncoderDisplayName);
        Assert.Equal(OverlayPlacementMode.WorldPin, signalLost.PlacementMode);
        Assert.Contains(signalLost.Alerts, alert =>
            alert.SemanticId == "signal.spout.unavailable");
        Assert.Contains(signalLost.Alerts, alert =>
            alert.SemanticId == "signal.desktop-audio.unavailable");
    }

    [Fact]
    public void UsesFinalPacketRateAndClearsOutsideRecordingStates()
    {
        var clock = new ControllableClock();
        var source = new ProductionWristTelemetrySource(clock);
        ((IRecordingMediaEventSink)source).Publish(new RecordingMediaProfile(
            640,
            360,
            VideoPixelFormat.Bgra8,
            30,
            640,
            360,
            30,
            EncoderKind.MediaFoundationSoftware,
            GpuVendor.Unknown));
        var recording = RecorderStatusSnapshot.Create(
            1,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.DesktopOnly));
        _ = source.Capture(
            recording,
            OverlayPlacementMode.WristDock,
            EnglishUiLocalizer.Instance);
        clock.Advance(TimeSpan.FromSeconds(2));
        ((IRecordingMediaEventSink)source).Publish(
            new RecordingSessionStatistics(
                90,
                90,
                100,
                0,
                0,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero));

        var stopping = Assert.IsType<WristTelemetrySnapshot>(source.Capture(
            RecorderStatusSnapshot.Create(2, RecorderState.Stopping),
            OverlayPlacementMode.WristDock,
            EnglishUiLocalizer.Instance));

        Assert.Equal(45, stopping.ActualFramesPerSecond);
        Assert.Null(source.Capture(
            RecorderStatusSnapshot.Create(3, RecorderState.Ready),
            OverlayPlacementMode.WristDock,
            EnglishUiLocalizer.Instance));
    }

    private sealed class ControllableClock : IMonotonicClock
    {
        public MonotonicTimestamp Now { get; private set; } =
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public void Advance(TimeSpan duration) => Now = Now.Add(duration);

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
