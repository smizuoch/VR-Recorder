using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ControllableVideoSignalGateway : IVideoSignalGateway
{
    private readonly TaskCompletionSource _requested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<StableVideoSignal> _signal = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<StableVideoSignal> WaitForStableSignalAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _requested.TrySetResult();
        return _signal.Task.WaitAsync(cancellationToken);
    }

    public Task WaitUntilRequestedAsync() => _requested.Task;

    public void CompleteWithTimeout() =>
        _signal.TrySetException(new TimeoutException("No stable video signal."));
}
