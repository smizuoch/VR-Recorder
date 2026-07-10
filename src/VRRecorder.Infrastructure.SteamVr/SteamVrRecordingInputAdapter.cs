using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrRecordingInputAdapter
{
    private readonly ISteamVrInputRuntime _runtime;
    private readonly IUiCommandDispatcher _commands;

    public SteamVrRecordingInputAdapter(
        ISteamVrInputRuntime runtime,
        IUiCommandDispatcher commands)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(commands);
        _runtime = runtime;
        _commands = commands;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var state in _runtime
                           .ObserveDigitalActionAsync(
                               RecordingInputContract.SteamVrToggleActionPath,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (!state.IsActive || !state.State || !state.Changed)
            {
                continue;
            }

            await _commands
                .DispatchAsync(
                    UiCommandId.ToggleRecording,
                    UiActivationKind.SteamVrAction,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
