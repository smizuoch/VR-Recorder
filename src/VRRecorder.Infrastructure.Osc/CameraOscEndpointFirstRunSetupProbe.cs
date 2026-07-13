using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Infrastructure.Osc;

public sealed class CameraOscEndpointFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private readonly IVrChatInstanceDiscovery _discovery;
    private readonly IVrChatCameraGatewayFactory _gateways;

    public CameraOscEndpointFirstRunSetupProbe(
        IVrChatInstanceDiscovery discovery,
        IVrChatCameraGatewayFactory gateways)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(gateways);
        _discovery = discovery;
        _gateways = gateways;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.CameraOscEndpoint)
        {
            return false;
        }

        var candidates = await _discovery.DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count != 1)
        {
            return false;
        }

        var gateway = _gateways.Create(candidates[0]);
        try
        {
            var snapshot = await gateway.ReadSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!snapshot.Mode.IsKnown)
            {
                return false;
            }

            await gateway.SetModeAsync(
                    snapshot.Mode.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            if (gateway is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (gateway is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
