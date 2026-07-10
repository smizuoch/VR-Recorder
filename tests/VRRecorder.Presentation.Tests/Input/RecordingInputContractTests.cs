using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Tests.Input;

public sealed class RecordingInputContractTests
{
    [Theory]
    [InlineData(UiActivationKind.DesktopClick)]
    [InlineData(UiActivationKind.DesktopKeyboard)]
    [InlineData(UiActivationKind.WristRay)]
    [InlineData(UiActivationKind.SteamVrAction)]
    public void EveryActivationKindResolvesToCanonicalToggleCommand(
        UiActivationKind activationKind)
    {
        Assert.Equal(
            UiCommandId.ToggleRecording,
            RecordingInputContract.Resolve(activationKind));
    }

    [Fact]
    public void SteamVrTogglePathMatchesActionManifestContract()
    {
        Assert.Equal(
            "/actions/vrrecorder/in/toggle_recording",
            RecordingInputContract.SteamVrToggleActionPath);
    }

    [Fact]
    public void UnknownActivationKindIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingInputContract.Resolve((UiActivationKind)int.MaxValue));
    }
}
