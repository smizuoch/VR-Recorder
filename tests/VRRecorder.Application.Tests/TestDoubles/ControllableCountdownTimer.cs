using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ControllableCountdownTimer : ICountdownTimer
{
    private readonly TaskCompletionSource _requested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(SelfTimer timer, CancellationToken cancellationToken)
    {
        _requested.TrySetResult();
        return _completed.Task.WaitAsync(cancellationToken);
    }

    public Task WaitUntilRequestedAsync() => _requested.Task;
}
