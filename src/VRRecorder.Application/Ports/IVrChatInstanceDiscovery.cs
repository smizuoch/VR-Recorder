using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Ports;

public interface IVrChatInstanceDiscovery
{
    Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
        CancellationToken cancellationToken);
}
