using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IVideoSignalGateway
{
    Task CaptureBaselineAsync(CancellationToken cancellationToken);

    Task<StableVideoSignal> WaitForStableSignalAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
