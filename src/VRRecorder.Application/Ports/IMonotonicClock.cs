namespace VRRecorder.Application.Ports;

public interface IMonotonicClock
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}
