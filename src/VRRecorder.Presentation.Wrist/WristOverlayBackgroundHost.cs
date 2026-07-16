using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristOverlayBackgroundHost
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromTicks(
        (TimeSpan.TicksPerSecond + 89) / 90);

    private readonly IRecorderStatusSource _statuses;
    private readonly WristUiProjector _projector;
    private readonly IWristOverlayInteractionTicker _ticker;
    private readonly IMonotonicClock _clock;
    private int _runStarted;

    public WristOverlayBackgroundHost(
        IRecorderStatusSource statuses,
        WristUiProjector projector,
        IWristOverlayInteractionTicker ticker,
        IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(clock);
        _statuses = statuses;
        _projector = projector;
        _ticker = ticker;
        _clock = clock;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _runStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "The wrist overlay background host has already started.");
        }
        var latest = new LatestStatus(_statuses.Current);
        using var subscription = _statuses.Subscribe(latest.Update);
        var nextDeadline = _clock.Now;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _clock.Now;
            var snapshot = _projector.Project(latest.Read());
            await _ticker
                .TickAsync(snapshot, now.Elapsed, cancellationToken)
                .ConfigureAwait(false);

            var afterTick = _clock.Now;
            nextDeadline = nextDeadline.Add(PollInterval);
            if (nextDeadline.Elapsed <= afterTick.Elapsed)
            {
                nextDeadline = afterTick.Add(PollInterval);
            }
            await _clock
                .DelayUntilAsync(nextDeadline, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class LatestStatus
    {
        private RecorderStatusSnapshot _value;

        public LatestStatus(RecorderStatusSnapshot initial)
        {
            ArgumentNullException.ThrowIfNull(initial);
            _value = initial;
        }

        public RecorderStatusSnapshot Read() => Volatile.Read(ref _value);

        public void Update(RecorderStatusSnapshot status)
        {
            ArgumentNullException.ThrowIfNull(status);
            Volatile.Write(ref _value, status);
        }
    }
}
