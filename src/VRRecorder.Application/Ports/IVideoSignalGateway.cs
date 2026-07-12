using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IVideoSignalGateway
{
    Task CaptureBaselineAsync(CancellationToken cancellationToken);

    Task<StableVideoSignal> WaitForStableSignalAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<StableVideoSignal> WaitForStableSignalAsync(
        string vrChatServiceId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        return WaitForStableSignalAsync(timeout, cancellationToken);
    }
}
