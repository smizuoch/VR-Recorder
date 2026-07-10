using VRRecorder.Application.Encoding;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Ports;

public interface IEncoderProbe
{
    Task<EncoderProbeResult> ProbeAsync(
        EncoderKind encoder,
        CancellationToken cancellationToken);
}
