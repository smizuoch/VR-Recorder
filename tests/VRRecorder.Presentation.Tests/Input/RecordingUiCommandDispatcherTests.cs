using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Tests.Input;

public sealed class RecordingUiCommandDispatcherTests
{
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
