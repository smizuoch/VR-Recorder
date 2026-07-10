using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Ports;

public interface ICountdownTimer
{
    Task WaitAsync(SelfTimer timer, CancellationToken cancellationToken);
}
