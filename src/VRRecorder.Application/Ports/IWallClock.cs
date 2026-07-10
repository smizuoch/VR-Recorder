namespace VRRecorder.Application.Ports;

public interface IWallClock
{
    DateTimeOffset LocalNow { get; }
}
