namespace VRRecorder.DesignSystem;

public sealed class RecordingInputDispatcher
{
    private readonly IUiCommandDispatcher _commands;

    public RecordingInputDispatcher(IUiCommandDispatcher commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public Task DispatchAsync(
        UiActivationKind activationKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var command = RecordingInputContract.Resolve(activationKind);
        return _commands.DispatchAsync(
            command,
            activationKind,
            cancellationToken);
    }
}
