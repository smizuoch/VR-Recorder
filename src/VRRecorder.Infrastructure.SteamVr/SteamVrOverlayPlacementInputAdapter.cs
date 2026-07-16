using VRRecorder.Application.Ports;
using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrOverlayPlacementInputAdapter
{
    private readonly ISteamVrInputRuntime _runtime;
    private readonly IWristOverlayPlacementCommands _commands;

    public SteamVrOverlayPlacementInputAdapter(
        ISteamVrInputRuntime runtime,
        IWristOverlayPlacementCommands commands)
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
                               WristOverlayInputContract
                                   .SteamVrRecenterActionPath,
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
                .RecenterAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
