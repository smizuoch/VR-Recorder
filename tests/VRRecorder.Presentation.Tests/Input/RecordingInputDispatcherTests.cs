using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Tests.Input;

public sealed class RecordingInputDispatcherTests
{
    [Theory]
    [InlineData(UiActivationKind.DesktopClick)]
    [InlineData(UiActivationKind.DesktopKeyboard)]
    public async Task DesktopActivationDispatchesOneCanonicalToggle(
        UiActivationKind activationKind)
    {
        var commands = new CapturingUiCommandDispatcher();
        var dispatcher = new RecordingInputDispatcher(commands);

        await dispatcher.DispatchAsync(
            activationKind,
            CancellationToken.None);

        var dispatched = Assert.Single(commands.Commands);
        Assert.Equal(UiCommandId.ToggleRecording, dispatched.Command);
        Assert.Equal(activationKind, dispatched.ActivationKind);
    }

    [Fact]
    public async Task CancellationPreventsDispatch()
    {
        var commands = new CapturingUiCommandDispatcher();
        var dispatcher = new RecordingInputDispatcher(commands);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(
                UiActivationKind.DesktopClick,
                cancellation.Token));

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task UnknownActivationIsRejectedWithoutDispatch()
    {
        var commands = new CapturingUiCommandDispatcher();
        var dispatcher = new RecordingInputDispatcher(commands);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            dispatcher.DispatchAsync(
                (UiActivationKind)int.MaxValue,
                CancellationToken.None));

        Assert.Empty(commands.Commands);
    }

    private sealed class CapturingUiCommandDispatcher : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Commands
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            Commands.Add((command, activationKind));
            return Task.CompletedTask;
        }
    }
}
