using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Tests.Input;

public sealed class RecordingUiCommandDispatcherTests
{
    [Theory]
    [InlineData(
        UiCommandId.ToggleMicrophone,
        UiActivationKind.DesktopKeyboard,
        "microphone")]
    [InlineData(
        UiCommandId.ToggleMuteAll,
        UiActivationKind.WristRay,
        "mute")]
    public async Task AudioCommandsRouteToTheirDedicatedOperations(
        UiCommandId command,
        UiActivationKind activationKind,
        string expectedRoute)
    {
        var routed = new List<(string Route, UiActivationKind Activation)>();
        var dispatcher = new RecordingUiCommandDispatcher(
            (activation, cancellationToken) =>
            {
                routed.Add(("recording", activation));
                return Task.CompletedTask;
            },
            (activation, cancellationToken) =>
            {
                routed.Add(("microphone", activation));
                return Task.CompletedTask;
            },
            (activation, cancellationToken) =>
            {
                routed.Add(("mute", activation));
                return Task.CompletedTask;
            });

        await dispatcher.DispatchAsync(
            command,
            activationKind,
            CancellationToken.None);

        Assert.Equal([(expectedRoute, activationKind)], routed);
    }

    [Fact]
    public async Task CanonicalToggleRoutesExactlyOnce()
    {
        var routed = new List<UiActivationKind>();
        var dispatcher = new RecordingUiCommandDispatcher(
            (activationKind, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                routed.Add(activationKind);
                return Task.CompletedTask;
            });

        await dispatcher.DispatchAsync(
            UiCommandId.ToggleRecording,
            UiActivationKind.DesktopClick,
            CancellationToken.None);

        Assert.Equal([UiActivationKind.DesktopClick], routed);
    }

    [Fact]
    public async Task NoSignalRetryRoutesToTheSameRecordingOperation()
    {
        var routed = new List<UiActivationKind>();
        var dispatcher = new RecordingUiCommandDispatcher(
            (activationKind, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                routed.Add(activationKind);
                return Task.CompletedTask;
            });

        await dispatcher.DispatchAsync(
            UiCommandId.Retry,
            UiActivationKind.WristRay,
            CancellationToken.None);

        Assert.Equal([UiActivationKind.WristRay], routed);
    }
}
