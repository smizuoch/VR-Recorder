using VRRecorder.Application.Audio;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristUiProjectorTests
{
    [Fact]
    public void CarriesValidatedRecordingTelemetryIntoWristSnapshot()
    {
        var telemetry = new WristTelemetrySnapshot(
            elapsedRecordingTime: TimeSpan.FromMinutes(1) +
                                  TimeSpan.FromSeconds(2),
            canvasWidth: 1920,
            canvasHeight: 1080,
            targetFramesPerSecond: 30,
            actualFramesPerSecond: 29.97,
            spoutSignal: WristSignalHealth.Available,
            desktopAudioSignal: WristSignalHealth.Degraded,
            microphoneSignal: WristSignalHealth.Unavailable,
            encoderDisplayName: "NVENC",
            placementMode: OverlayPlacementMode.WristDock,
            alerts:
            [
                new WristAlertSnapshot(
                    "audio.microphone.unavailable",
                    WristAlertSeverity.Warning,
                    new LocalizedText(
                        "audio.microphone.unavailable",
                        "Microphone unavailable")),
            ]);
        var status = RecorderStatusSnapshot.Create(
            12,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Mixed));

        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(status, WristPage.Main, telemetry);

        Assert.Same(telemetry, snapshot.Telemetry);
        Assert.Equal("01:02", telemetry.ElapsedText);
        Assert.Equal("1920×1080", telemetry.ResolutionText);
        Assert.Equal("30 / 29.97 FPS", telemetry.FramesPerSecondText);
        Assert.Equal(WristSignalHealth.Available, telemetry.SpoutSignal);
        Assert.Equal(
            WristSignalHealth.Degraded,
            telemetry.DesktopAudioSignal);
        Assert.Equal(
            WristSignalHealth.Unavailable,
            telemetry.MicrophoneSignal);
        Assert.Equal(OverlayPlacementMode.WristDock, telemetry.PlacementMode);
        Assert.Equal("NVENC", telemetry.EncoderDisplayName);
        var alert = Assert.Single(telemetry.Alerts);
        Assert.Equal(WristAlertSeverity.Warning, alert.Severity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1000.01)]
    public void TelemetryRejectsInvalidTargetFramesPerSecond(double fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristTelemetrySnapshot(
                TimeSpan.Zero,
                1920,
                1080,
                fps,
                0,
                WristSignalHealth.Available,
                WristSignalHealth.Available,
                WristSignalHealth.Available,
                "H.264",
                OverlayPlacementMode.WristDock,
                []));
    }

    [Fact]
    public void RecordingMainProjectsStopMicrophoneAndMuteControls()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = RecorderStatusSnapshot.Create(
            10,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(
                AudioRouting.DesktopOnly));

        var snapshot = projector.Project(status, WristPage.Main);

        Assert.Equal(3, snapshot.Actions.Count);
        var stop = Assert.Single(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleRecording);
        Assert.True(stop.MinimumTargetDp >= 64);
        var microphone = Assert.Single(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleMicrophone);
        Assert.Equal("audio.microphone.off", microphone.SemanticId);
        Assert.Equal(
            UiComponentRole.FilledTonalIconToggleButton,
            microphone.ComponentRole);
        Assert.False(microphone.IsSelected);
        Assert.Equal("Microphone off", microphone.AccessibleName.Value);
        Assert.Equal(
            "Do not record microphone audio",
            microphone.Tooltip.Value);
        Assert.True(microphone.MinimumTargetDp >= 56);
        var mute = Assert.Single(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleMuteAll);
        Assert.Equal("audio.muteAll", mute.SemanticId);
        Assert.Equal(UiComponentRole.IconToggleButton, mute.ComponentRole);
        Assert.False(mute.IsSelected);
        Assert.Equal(
            "Mute desktop audio and microphone",
            mute.AccessibleName.Value);
        Assert.Equal("Turn off all recorded audio", mute.Tooltip.Value);
        Assert.True(mute.MinimumTargetDp >= 56);
    }

    [Fact]
    public void MutedRecordingRetainsMicrophoneRestoreSelection()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = RecorderStatusSnapshot.Create(
            11,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Muted));

        var snapshot = projector.Project(status, WristPage.Main);

        var microphone = Assert.Single(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleMicrophone);
        var mute = Assert.Single(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleMuteAll);
        Assert.Equal("audio.microphone.on", microphone.SemanticId);
        Assert.True(microphone.IsSelected);
        Assert.True(mute.IsSelected);
        Assert.True(microphone.IsEnabled);
        Assert.True(mute.IsEnabled);
    }

    [Theory]
    [InlineData(RecorderState.Booting, UiColorRole.Surface)]
    [InlineData(RecorderState.ComplianceFault, UiColorRole.Error)]
    [InlineData(RecorderState.Ready, UiColorRole.Surface)]
    [InlineData(RecorderState.Arming, UiColorRole.Surface)]
    [InlineData(RecorderState.Countdown, UiColorRole.Surface)]
    [InlineData(RecorderState.Starting, UiColorRole.Surface)]
    [InlineData(RecorderState.Recording, UiColorRole.Recording)]
    [InlineData(RecorderState.SignalLost, UiColorRole.Error)]
    [InlineData(RecorderState.Stopping, UiColorRole.Surface)]
    [InlineData(RecorderState.NoSignal, UiColorRole.Error)]
    [InlineData(RecorderState.Faulted, UiColorRole.Error)]
    public void EveryRecorderStateHasNonColorSemanticCue(
        RecorderState state,
        UiColorRole expectedColorRole)
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 5,
            State: state,
            AvailableActions: RecorderAvailableActions.None);

        var snapshot = projector.Project(status);

        Assert.Equal(expectedColorRole, snapshot.StateCue.ColorRole);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StateCue.IconSemanticId));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StateCue.Label.Value));
        Assert.StartsWith(
            "state.",
            snapshot.StateCue.Label.ResourceKey,
            StringComparison.Ordinal);
        Assert.NotEqual(UiColorRole.Error, UiColorRole.Recording);
    }

    [Fact]
    public void NoSignalSuppressesStartAndProjectsAccessibleRetry()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 3,
            State: RecorderState.NoSignal,
            AvailableActions:
                RecorderAvailableActions.Start | RecorderAvailableActions.Retry);

        var snapshot = projector.Project(status);

        Assert.DoesNotContain(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleRecording);
        var retry = Assert.Single(snapshot.Actions);
        Assert.Equal(UiCommandId.Retry, retry.Command);
        Assert.Equal(UiComponentRole.FilledTonalButton, retry.ComponentRole);
        Assert.Equal(UiColorRole.Error, retry.ColorRole);
        Assert.Equal("RETRY", retry.VisibleLabel.Value);
        Assert.Equal("Retry camera connection", retry.AccessibleName.Value);
    }

    [Theory]
    [InlineData(RecorderState.Faulted)]
    [InlineData(RecorderState.ComplianceFault)]
    public void FaultStatesNeverProjectStart(RecorderState state)
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 4,
            State: state,
            AvailableActions: RecorderAvailableActions.Start);

        var snapshot = projector.Project(status);

        Assert.DoesNotContain(snapshot.Actions, action =>
            action.Command == UiCommandId.ToggleRecording);
    }

    [Theory]
    [InlineData(WristPage.Main)]
    [InlineData(WristPage.Settings)]
    [InlineData(WristPage.Legal)]
    [InlineData(WristPage.Positioning)]
    public void RecordingKeepsCriticalStopReachableFromEveryPage(WristPage page)
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 2,
            State: RecorderState.Recording,
            AvailableActions: RecorderAvailableActions.Stop);

        var snapshot = projector.Project(status, page);

        var action = Assert.Single(snapshot.Actions, item =>
            item.SemanticId == "recording.stop");
        Assert.Equal(UiCommandId.ToggleRecording, action.Command);
        Assert.True(action.IsEnabled);
        Assert.Equal("STOP", action.VisibleLabel.Value);
        Assert.Equal("Stop recording", action.AccessibleName.Value);
        Assert.Equal("Stop recording", action.Tooltip.Value);
        Assert.True(action.MinimumTargetDp >= 64);
        Assert.Equal(page, snapshot.Page);
    }

    [Fact]
    public void PositioningPageExposesNudgeRecenterAndBackActions()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                new RecorderStatusSnapshot(
                    Revision: 3,
                    RecorderState.Ready,
                    RecorderAvailableActions.Start),
                WristPage.Positioning);

        Assert.Equal(
            [
                UiCommandId.NudgeOverlayUp,
                UiCommandId.NudgeOverlayDown,
                UiCommandId.NudgeOverlayLeft,
                UiCommandId.NudgeOverlayRight,
                UiCommandId.RecenterOverlay,
                UiCommandId.CloseOverlayPositioning,
            ],
            snapshot.Actions.Select(action => action.Command));
        Assert.All(snapshot.Actions, action =>
        {
            Assert.True(action.IsEnabled);
            Assert.True(action.MinimumTargetDp >= 56);
            Assert.False(string.IsNullOrWhiteSpace(
                action.AccessibleName.Value));
            Assert.False(string.IsNullOrWhiteSpace(action.Tooltip.Value));
        });
    }

    [Fact]
    public void ReadyProjectsOneEnabledAccessibleRecordAction()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 1,
            State: RecorderState.Ready,
            AvailableActions: RecorderAvailableActions.Start);

        var snapshot = projector.Project(status);

        var action = Assert.Single(snapshot.Actions, item =>
            item.SemanticId == "recording.start");
        Assert.Equal(UiCommandId.ToggleRecording, action.Command);
        Assert.Equal("recording.start", action.IconSemanticId);
        Assert.Equal(
            UiComponentRole.LargeFilledIconButton,
            action.ComponentRole);
        Assert.Equal(UiColorRole.Recording, action.ColorRole);
        Assert.True(action.IsEnabled);
        Assert.Equal("REC", action.VisibleLabel.Value);
        Assert.Equal("Start recording", action.AccessibleName.Value);
        Assert.Equal("Start recording", action.Tooltip.Value);
        Assert.True(action.MinimumTargetDp >= 56);
    }

    [Fact]
    public void SignalLostKeepsAccessibleStopAvailable()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 9,
            State: RecorderState.SignalLost,
            AvailableActions: RecorderAvailableActions.Stop);

        var snapshot = projector.Project(status, WristPage.Legal);

        var stop = Assert.Single(snapshot.Actions);
        Assert.Equal(UiCommandId.ToggleRecording, stop.Command);
        Assert.Equal("recording.stop", stop.SemanticId);
        Assert.Equal(UiColorRole.Recording, stop.ColorRole);
        Assert.True(stop.IsEnabled);
        Assert.Equal("STOP", stop.VisibleLabel.Value);
        Assert.Equal("Stop recording", stop.AccessibleName.Value);
        Assert.True(stop.MinimumTargetDp >= 64);
    }
}
