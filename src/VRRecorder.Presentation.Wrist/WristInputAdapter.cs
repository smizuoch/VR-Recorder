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

    public async Task<bool> ActivateAtAsync(
        WristUiSnapshot snapshot,
        WristTextureLayout layout,
        int pixelX,
        int pixelY,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layout);
        cancellationToken.ThrowIfCancellationRequested();
        var target = layout.HitTest(pixelX, pixelY);
        if (target is null)
        {
            return false;
        }

        UiActionSnapshot? matchedAction = null;
        foreach (var action in snapshot.Actions)
        {
            if (!StringComparer.Ordinal.Equals(
                    action.SemanticId,
                    target.SemanticId))
            {
                continue;
            }

            if (matchedAction is not null)
            {
                return false;
            }
            matchedAction = action;
        }

        if (matchedAction is null ||
            !matchedAction.IsEnabled ||
            matchedAction.Command != target.Command)
        {
            return false;
        }

        await ActivateAsync(matchedAction, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
