using VRRecorder.Application.Audio;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopAudioAvailabilityUiControllerTests
{
    [Fact]
    public void LossesAndRecoveriesRemainIndependentAndAnnounceInOrder()
    {
        var controller = new DesktopAudioAvailabilityUiController();
        controller.Apply(Status(revision: 1, RecorderState.Starting));

        var desktopLost = controller.Apply(Warning(
            revision: 10,
            AudioInput.Desktop));
        var microphoneLost = controller.Apply(Warning(
            revision: 11,
            AudioInput.Microphone));
        var desktopRecovered = controller.Apply(Recovered(
            revision: 12,
            AudioInput.Desktop));
        var microphoneRecovered = controller.Apply(Recovered(
            revision: 13,
            AudioInput.Microphone));

        AssertSnapshot(
            desktopLost,
            AudioInputAvailability.Desktop,
            "Recording_Notification_Audio_DesktopUnavailable",
            "Recording_Notification_Audio_DesktopUnavailable",
            DesktopAnnouncementUrgency.Assertive);
        AssertSnapshot(
            microphoneLost,
            AudioInputAvailability.All,
            "Recording_Notification_Audio_BothUnavailable",
            "Recording_Notification_Audio_MicrophoneUnavailable",
            DesktopAnnouncementUrgency.Assertive);
        AssertSnapshot(
            desktopRecovered,
            AudioInputAvailability.Microphone,
            "Recording_Notification_Audio_MicrophoneUnavailable",
            "Recording_Notification_Audio_DesktopRecovered",
            DesktopAnnouncementUrgency.Polite);
        AssertSnapshot(
            microphoneRecovered,
            AudioInputAvailability.None,
            "Recording_Notification_Audio_MicrophoneRecovered",
            "Recording_Notification_Audio_MicrophoneRecovered",
            DesktopAnnouncementUrgency.Polite);
        Assert.Same(microphoneRecovered, controller.Current);
    }

    [Fact]
    public void DuplicateAndOutOfOrderNotificationsDoNotReannounce()
    {
        var controller = new DesktopAudioAvailabilityUiController();
        controller.Apply(Status(revision: 1, RecorderState.Recording));
        var first = controller.Apply(Warning(
            revision: 20,
            AudioInput.Desktop));

        var duplicate = controller.Apply(Warning(
            revision: 21,
            AudioInput.Desktop));
        var staleRecovery = controller.Apply(Recovered(
            revision: 19,
            AudioInput.Desktop));
        var rediscoveryFailure = controller.Apply(Warning(
            revision: 22,
            AudioInput.Desktop,
            AudioSessionWarningKind.EndpointRediscoveryFailed));

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.Null(staleRecovery);
        Assert.Null(rediscoveryFailure);
        Assert.Same(first, controller.Current);
    }

    [Fact]
    public void SessionStatesGateFreezeAndClearAudioAvailability()
    {
        var controller = new DesktopAudioAvailabilityUiController();
        controller.Apply(Status(revision: 1, RecorderState.Arming));
        Assert.Null(controller.Apply(Warning(
            revision: 10,
            AudioInput.Desktop)));
        controller.Apply(Status(revision: 2, RecorderState.Countdown));
        Assert.Null(controller.Apply(Warning(
            revision: 11,
            AudioInput.Desktop)));
        controller.Apply(Status(revision: 3, RecorderState.Starting));
        var visible = controller.Apply(Warning(
            revision: 12,
            AudioInput.Desktop));
        Assert.NotNull(visible);

        controller.Apply(Status(revision: 4, RecorderState.Stopping));
        Assert.Null(controller.Apply(Recovered(
            revision: 13,
            AudioInput.Desktop)));
        Assert.Same(visible, controller.Current);
        var hidden = controller.Apply(Status(
            revision: 5,
            RecorderState.Ready));
        AssertSnapshot(
            hidden,
            AudioInputAvailability.None,
            displayResourceKey: null,
            announcementResourceKey: null,
            DesktopAnnouncementUrgency.None);
        Assert.Null(controller.Apply(Warning(
            revision: 14,
            AudioInput.Microphone)));
        Assert.Same(hidden, controller.Current);
    }

    [Theory]
    [InlineData(RecorderState.Ready)]
    [InlineData(RecorderState.NoSignal)]
    [InlineData(RecorderState.Faulted)]
    [InlineData(RecorderState.ComplianceFault)]
    public void NonActiveBoundaryClearsVisibleAudioState(RecorderState state)
    {
        var controller = new DesktopAudioAvailabilityUiController();
        controller.Apply(Status(revision: 1, RecorderState.SignalLost));
        controller.Apply(Warning(revision: 10, AudioInput.Microphone));

        var hidden = controller.Apply(Status(revision: 2, state));

        AssertSnapshot(
            hidden,
            AudioInputAvailability.None,
            displayResourceKey: null,
            announcementResourceKey: null,
            DesktopAnnouncementUrgency.None);
        Assert.Null(controller.Apply(Warning(
            revision: 11,
            AudioInput.Microphone)));
    }

    [Fact]
    public void StaleStatusAndTerminalStateCannotReopenAudioSession()
    {
        var controller = new DesktopAudioAvailabilityUiController();
        controller.Apply(Status(revision: 5, RecorderState.Faulted));

        Assert.Null(controller.Apply(Status(
            revision: 4,
            RecorderState.Starting)));
        Assert.Null(controller.Apply(Status(
            revision: 6,
            RecorderState.Starting)));
        Assert.Null(controller.Apply(Warning(
            revision: 10,
            AudioInput.Desktop)));
        Assert.Null(controller.Current);
    }

    private static void AssertSnapshot(
        DesktopAudioAvailabilityUiSnapshot? snapshot,
        AudioInputAvailability expectedUnavailableInputs,
        string? displayResourceKey,
        string? announcementResourceKey,
        DesktopAnnouncementUrgency urgency)
    {
        Assert.NotNull(snapshot);
        Assert.Equal(expectedUnavailableInputs, snapshot.UnavailableInputs);
        Assert.Equal(displayResourceKey, snapshot.DisplayResourceKey);
        Assert.Equal(
            announcementResourceKey,
            snapshot.AnnouncementResourceKey);
        Assert.Equal(urgency, snapshot.AnnouncementUrgency);
    }

    private static RecorderStatusSnapshot Status(
        long revision,
        RecorderState state) => RecorderStatusSnapshot.Create(revision, state);

    private static DesktopRecordingNotification.AudioWarning Warning(
        long revision,
        AudioInput input,
        AudioSessionWarningKind kind =
            AudioSessionWarningKind.InputUnavailable) =>
        new(
            revision,
            new AudioSessionWarning(kind, input, FramePosition: 4_800));

    private static DesktopRecordingNotification.AudioRecovered Recovered(
        long revision,
        AudioInput input) =>
        new(
            revision,
            new AudioSessionStatus(
                AudioSessionStatusKind.InputRecovered,
                input,
                FramePosition: 9_600));
}
