namespace VRRecorder.DesignSystem;

public sealed class RecordingUiCommandDispatcher : IUiCommandDispatcher
{
    private readonly Func<
        UiActivationKind,
        CancellationToken,
        Task> _toggleRecording;
    private readonly Func<
        UiActivationKind,
        CancellationToken,
        Task>? _toggleMicrophone;
    private readonly Func<
        UiActivationKind,
        CancellationToken,
        Task>? _toggleMuteAll;

    public RecordingUiCommandDispatcher(
        Func<UiActivationKind, CancellationToken, Task> toggleRecording)
    {
        ArgumentNullException.ThrowIfNull(toggleRecording);
        _toggleRecording = toggleRecording;
    }

    public RecordingUiCommandDispatcher(
        Func<UiActivationKind, CancellationToken, Task> toggleRecording,
        Func<UiActivationKind, CancellationToken, Task> toggleMicrophone,
        Func<UiActivationKind, CancellationToken, Task> toggleMuteAll)
    {
        ArgumentNullException.ThrowIfNull(toggleRecording);
        ArgumentNullException.ThrowIfNull(toggleMicrophone);
        ArgumentNullException.ThrowIfNull(toggleMuteAll);
        _toggleRecording = toggleRecording;
        _toggleMicrophone = toggleMicrophone;
        _toggleMuteAll = toggleMuteAll;
    }

    public Task DispatchAsync(
        UiCommandId command,
        UiActivationKind activationKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return command switch
        {
            UiCommandId.ToggleRecording or UiCommandId.Retry =>
                _toggleRecording(activationKind, cancellationToken),
            UiCommandId.ToggleMicrophone when _toggleMicrophone is not null =>
                _toggleMicrophone(activationKind, cancellationToken),
            UiCommandId.ToggleMuteAll when _toggleMuteAll is not null =>
                _toggleMuteAll(activationKind, cancellationToken),
            _ => throw new NotSupportedException(
                $"UI command {command} is not supported by the recording dispatcher."),
        };
    }
}
