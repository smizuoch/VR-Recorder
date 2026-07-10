namespace VRRecorder.DesignSystem;

public sealed class RecordingUiCommandDispatcher : IUiCommandDispatcher
{
    private readonly Func<
        UiActivationKind,
        CancellationToken,
        Task> _toggleRecording;

    public RecordingUiCommandDispatcher(
        Func<UiActivationKind, CancellationToken, Task> toggleRecording)
    {
        ArgumentNullException.ThrowIfNull(toggleRecording);
        _toggleRecording = toggleRecording;
    }

    public Task DispatchAsync(
        UiCommandId command,
        UiActivationKind activationKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command != UiCommandId.ToggleRecording)
        {
            throw new NotSupportedException(
                $"UI command {command} is not supported by the recording dispatcher.");
        }

        return _toggleRecording(activationKind, cancellationToken);
    }
}
