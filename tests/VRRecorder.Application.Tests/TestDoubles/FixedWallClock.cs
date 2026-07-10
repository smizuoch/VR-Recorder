using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FixedWallClock : IWallClock
{
    public FixedWallClock(DateTimeOffset localNow)
    {
        LocalNow = localNow;
    }

    public DateTimeOffset LocalNow { get; }
}
