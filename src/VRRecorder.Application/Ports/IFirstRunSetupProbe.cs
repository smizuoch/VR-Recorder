using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Ports;

public interface IFirstRunSetupProbe
{
    Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken);
}
