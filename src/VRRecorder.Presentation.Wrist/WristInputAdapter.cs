using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristInputAdapter
{
    private readonly IUiCommandDispatcher _commands;
    private readonly RecordingInputDispatcher _recordingInputs;

    public WristInputAdapter(IUiCommandDispatcher commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _recordingInputs = new RecordingInputDispatcher(commands);
    }

    public Task ActivateAsync(
        UiActionSnapshot action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (!action.IsEnabled)
        {
            return Task.CompletedTask;
        }

        return action.Command == UiCommandId.ToggleRecording
            ? _recordingInputs.DispatchAsync(
                UiActivationKind.WristRay,
                cancellationToken)
            : _commands.DispatchAsync(
                action.Command,
                UiActivationKind.WristRay,
                cancellationToken);
    }
}
