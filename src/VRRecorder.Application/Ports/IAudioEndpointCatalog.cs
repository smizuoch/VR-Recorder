using VRRecorder.Application.Audio;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Ports;

public interface IAudioEndpointCatalog
{
    Task<IReadOnlyList<AudioEndpointOption>> GetActiveAsync(
        AudioInput input,
        CancellationToken cancellationToken);
}
