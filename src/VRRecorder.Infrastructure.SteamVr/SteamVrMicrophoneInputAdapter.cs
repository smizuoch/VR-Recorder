using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrMicrophoneInputAdapter
{
    private readonly ISteamVrInputRuntime _runtime;
    private readonly IUiCommandDispatcher _commands;

    public SteamVrMicrophoneInputAdapter(
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
        var pressIsLatched = false;
        await foreach (var state in _runtime
                           .ObserveDigitalActionAsync(
                               RecordingInputContract
                                   .SteamVrToggleMicrophoneActionPath,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (!state.IsActive || !state.State)
            {
                pressIsLatched = false;
                continue;
            }

            if (!state.Changed || pressIsLatched)
            {
                continue;
            }

            pressIsLatched = true;
            await _commands
                .DispatchAsync(
                    UiCommandId.ToggleMicrophone,
                    UiActivationKind.SteamVrAction,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
