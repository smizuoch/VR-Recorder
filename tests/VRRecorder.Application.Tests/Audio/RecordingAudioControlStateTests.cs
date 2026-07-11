using VRRecorder.Application.Audio;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.Audio;

public sealed class RecordingAudioControlStateTests
{
    [Theory]
    [InlineData(AudioRouting.Mixed, true, true, false)]
    [InlineData(AudioRouting.DesktopOnly, true, false, false)]
    [InlineData(AudioRouting.MicOnly, false, true, false)]
    [InlineData(AudioRouting.Muted, true, true, true)]
    public void CreatesDeterministicControlStateFromInitialRouting(
        AudioRouting routing,
        bool desktopIncluded,
        bool microphoneIncluded,
        bool muteAll)
    {
        var state = RecordingAudioControlState.FromRouting(routing);

        Assert.Equal(desktopIncluded, state.DesktopIncluded);
        Assert.Equal(microphoneIncluded, state.MicrophoneIncluded);
        Assert.Equal(muteAll, state.MuteAll);
        Assert.Equal(routing, state.EffectiveRouting);
    }

    [Fact]
    public void MicrophoneToggleNeverChangesDesktopSelection()
    {
        var mixed = RecordingAudioControlState.FromRouting(
            AudioRouting.Mixed);

        var microphoneOff = mixed.Apply(
            RecordingAudioCommand.ToggleMicrophone);
        var microphoneOn = microphoneOff.Apply(
            RecordingAudioCommand.ToggleMicrophone);

        Assert.True(microphoneOff.DesktopIncluded);
        Assert.False(microphoneOff.MicrophoneIncluded);
        Assert.Equal(AudioRouting.DesktopOnly, microphoneOff.EffectiveRouting);
        Assert.Equal(mixed, microphoneOn);
    }

    [Fact]
    public void MuteOverridePreservesAndRestoresInputSelection()
    {
        var desktopOnly = RecordingAudioControlState.FromRouting(
            AudioRouting.DesktopOnly);

        var muted = desktopOnly.Apply(RecordingAudioCommand.ToggleMuteAll);
        var restored = muted.Apply(RecordingAudioCommand.ToggleMuteAll);

        Assert.True(muted.MuteAll);
        Assert.Equal(AudioRouting.Muted, muted.EffectiveRouting);
        Assert.Equal(desktopOnly, restored);
    }

    [Fact]
    public void MicrophoneToggleWhileMutedChangesOnlyTheRestoreTarget()
    {
        var muted = RecordingAudioControlState.FromRouting(AudioRouting.Muted);

        var microphoneOff = muted.Apply(
            RecordingAudioCommand.ToggleMicrophone);
        var unmuted = microphoneOff.Apply(
            RecordingAudioCommand.ToggleMuteAll);

        Assert.True(microphoneOff.MuteAll);
        Assert.True(microphoneOff.DesktopIncluded);
        Assert.False(microphoneOff.MicrophoneIncluded);
        Assert.Equal(AudioRouting.Muted, microphoneOff.EffectiveRouting);
        Assert.False(unmuted.MuteAll);
        Assert.Equal(AudioRouting.DesktopOnly, unmuted.EffectiveRouting);
    }

    [Fact]
    public void MicrophoneOnlyCanBecomeSilentWithoutSelectingMuteOverride()
    {
        var microphoneOnly = RecordingAudioControlState.FromRouting(
            AudioRouting.MicOnly);

        var silent = microphoneOnly.Apply(
            RecordingAudioCommand.ToggleMicrophone);

        Assert.False(silent.DesktopIncluded);
        Assert.False(silent.MicrophoneIncluded);
        Assert.False(silent.MuteAll);
        Assert.Equal(AudioRouting.Muted, silent.EffectiveRouting);
    }

    [Fact]
    public void RejectsUnknownRoutingAndCommand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingAudioControlState.FromRouting((AudioRouting)999));
        var state = RecordingAudioControlState.FromRouting(AudioRouting.Mixed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.Apply((RecordingAudioCommand)999));
    }
}
