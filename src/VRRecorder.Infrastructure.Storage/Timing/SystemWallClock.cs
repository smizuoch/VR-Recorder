using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Storage;

public sealed class SystemWallClock : IWallClock
{
    private readonly TimeProvider _timeProvider;

    public SystemWallClock()
        : this(TimeProvider.System)
    {
    }

    public SystemWallClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    public static SystemWallClock Instance { get; } = new();

    public DateTimeOffset LocalNow => _timeProvider.GetLocalNow();
}
