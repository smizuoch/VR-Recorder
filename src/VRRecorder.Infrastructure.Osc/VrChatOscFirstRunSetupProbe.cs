using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Infrastructure.Osc;

public sealed class VrChatOscFirstRunSetupProbe : IFirstRunSetupProbe
{
    private readonly IVrChatInstanceDiscovery _discovery;

    public VrChatOscFirstRunSetupProbe(IVrChatInstanceDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _discovery = discovery;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.VrChatOscDetection)
        {
            return false;
        }

        var candidates = await _discovery.DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Count > 0;
    }
}
