using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IVideoSignalGateway
{
    Task<StableVideoSignal> WaitForStableSignalAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
