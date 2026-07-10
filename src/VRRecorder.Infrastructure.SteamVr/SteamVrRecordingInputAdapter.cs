using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrRecordingInputAdapter
{
    private readonly ISteamVrInputRuntime _runtime;
    private readonly RecordingInputDispatcher _inputs;

    public SteamVrRecordingInputAdapter(
        ISteamVrInputRuntime runtime,
        RecordingInputDispatcher inputs)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(inputs);
        _runtime = runtime;
        _inputs = inputs;
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

            await _inputs
                .DispatchAsync(
                    UiActivationKind.SteamVrAction,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
