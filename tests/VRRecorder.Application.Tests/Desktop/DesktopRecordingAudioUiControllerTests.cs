using VRRecorder.Application.Audio;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingAudioUiControllerTests
{
    [Theory]
    [InlineData(RecorderState.Recording)]
    [InlineData(RecorderState.SignalLost)]
    public void ActiveRecordingProjectsEnabledInputSelections(
        RecorderState state)
    {
        var controller = new DesktopRecordingAudioUiController();
        var status = RecorderStatusSnapshot.Create(
            4,
            state,
            RecordingAudioControlState.FromRouting(
                AudioRouting.DesktopOnly));

        var snapshot = controller.Apply(status);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsVisible);
        Assert.True(snapshot.IsEnabled);
        Assert.False(snapshot.IsMicrophoneSelected);
        Assert.False(snapshot.IsMuteAllSelected);
        Assert.Equal(
            "Microphone_Off_AccessibleName",
            snapshot.MicrophoneAccessibleNameResourceKey);
        Assert.Equal(
            "Microphone_Off_Tooltip",
            snapshot.MicrophoneHelpResourceKey);
        Assert.Equal(
            "Audio_MuteAll_AccessibleName",
            snapshot.MuteAllAccessibleNameResourceKey);
        Assert.Equal(
            "Audio_MuteAll_Tooltip",
            snapshot.MuteAllHelpResourceKey);
    }

    [Fact]
    public void MutedStateKeepsMicrophoneRestoreSelectionVisible()
    {
        var controller = new DesktopRecordingAudioUiController();

        var snapshot = controller.Apply(RecorderStatusSnapshot.Create(
            5,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Muted)));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsMicrophoneSelected);
        Assert.True(snapshot.IsMuteAllSelected);
        Assert.Equal(
            "Microphone_On_AccessibleName",
            snapshot.MicrophoneAccessibleNameResourceKey);
        Assert.Equal(
            "Microphone_On_Tooltip",
            snapshot.MicrophoneHelpResourceKey);
    }

    [Fact]
    public void StoppingFreezesSelectionsButDisablesCommands()
    {
        var controller = new DesktopRecordingAudioUiController();
        var state = new RecordingAudioControlState(
            DesktopIncluded: true,
            MicrophoneIncluded: false,
            MuteAll: true);

        var snapshot = controller.Apply(RecorderStatusSnapshot.Create(
            6,
            RecorderState.Stopping,
            state));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsVisible);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.IsMicrophoneSelected);
        Assert.True(snapshot.IsMuteAllSelected);
    }

    [Fact]
    public void InactiveRecorderHidesAudioCommandsAndRejectsStaleRevision()
    {
        var controller = new DesktopRecordingAudioUiController();

        var ready = controller.Apply(RecorderStatusSnapshot.Create(
            7,
            RecorderState.Ready));
        var stale = controller.Apply(RecorderStatusSnapshot.Create(
            6,
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Mixed)));

        Assert.NotNull(ready);
        Assert.False(ready.IsVisible);
        Assert.False(ready.IsEnabled);
        Assert.Null(stale);
        Assert.Equal(ready, controller.Current);
    }
}
