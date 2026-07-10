using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Timing;

public sealed class ProductionTimeAdapterTests
{
    [Fact]
    public void MonotonicClockStartsAtZeroAndIgnoresWallClockRollback()
    {
        var provider = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var clock = new SystemMonotonicClock(provider);

        Assert.Equal(TimeSpan.Zero, clock.Now.Elapsed);

        provider.Advance(TimeSpan.FromSeconds(7));
        provider.SetUtcNow(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(7), clock.Now.Elapsed);
    }

    [Fact]
    public void MonotonicClockClampsInvalidBackwardSourceMovementToZero()
    {
        var provider = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new SystemMonotonicClock(provider);

        provider.SetTimestamp(-1);

        Assert.Equal(TimeSpan.Zero, clock.Now.Elapsed);
    }

    [Fact]
    public async Task MonotonicClockCompletesPastDeadlineWithoutCreatingTimer()
    {
        var provider = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new SystemMonotonicClock(provider);
        provider.Advance(TimeSpan.FromSeconds(5));

        await clock.DelayUntilAsync(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(4)),
            CancellationToken.None);

        Assert.Equal(0, provider.TimerCreationCount);
    }

    [Fact]
    public async Task MonotonicClockFutureDeadlineUsesTimestampAndIsCancellable()
    {
        var provider = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new SystemMonotonicClock(provider);
        using var cancellation = new CancellationTokenSource();

        var delay = clock.DelayUntilAsync(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(3)),
            cancellation.Token);

        Assert.False(delay.IsCompleted);
        Assert.Equal(TimeSpan.FromSeconds(3), provider.LastDueTime);

        provider.Advance(TimeSpan.FromSeconds(2));
        Assert.False(delay.IsCompleted);

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => delay);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task MonotonicClockChunksDeadlineBeyondPlatformTimerLimit()
    {
        var provider = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new SystemMonotonicClock(provider);
        using var cancellation = new CancellationTokenSource();

        var delay = clock.DelayUntilAsync(
            MonotonicTimestamp.FromElapsed(TimeSpan.MaxValue),
            cancellation.Token);

        Assert.False(delay.IsCompleted);
        Assert.InRange(
            provider.LastDueTime,
            TimeSpan.FromDays(1),
            TimeSpan.FromMilliseconds(int.MaxValue));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delay);
    }

    [Fact]
    public void WallClockReturnsProviderLocalTime()
    {
        var utcNow = new DateTimeOffset(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.Zero);
        var provider = new ManualTimeProvider(
            utcNow,
            TimeZoneInfo.CreateCustomTimeZone(
                "Test/Japan",
                TimeSpan.FromHours(9),
                "Test/Japan",
                "Test/Japan"));

        var clock = new SystemWallClock(provider);

        Assert.Equal(utcNow.ToOffset(TimeSpan.FromHours(9)), clock.LocalNow);
    }

    [Fact]
    public async Task CountdownOffDoesNotReadOrDelayClock()
    {
        var clock = new TrackingMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var countdown = new MonotonicCountdownTimer(clock);

        await countdown.WaitAsync(
            SelfTimer.FromSeconds(0),
            new CancellationToken(canceled: true));

        Assert.Equal(0, clock.NowReadCount);
        Assert.Equal(0, clock.DelayCallCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task CountdownDelaysUntilNowPlusConfiguredDuration(int seconds)
    {
        var now = MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(42));
        var clock = new TrackingMonotonicClock(now);
        var countdown = new MonotonicCountdownTimer(clock);
        using var cancellation = new CancellationTokenSource();

        await countdown.WaitAsync(
            SelfTimer.FromSeconds(seconds),
            cancellation.Token);

        Assert.Equal(1, clock.NowReadCount);
        Assert.Equal(1, clock.DelayCallCount);
        Assert.Equal(
            now.Add(TimeSpan.FromSeconds(seconds)),
            clock.RequestedDeadline);
        Assert.Equal(cancellation.Token, clock.RequestedCancellationToken);
    }

    [Fact]
    public async Task CountdownSaturatesDeadlineAtMaximumTimestamp()
    {
        var now = MonotonicTimestamp.FromElapsed(
            TimeSpan.MaxValue - TimeSpan.FromSeconds(2));
        var clock = new TrackingMonotonicClock(now);
        var countdown = new MonotonicCountdownTimer(clock);

        await countdown.WaitAsync(
            SelfTimer.FromSeconds(10),
            CancellationToken.None);

        Assert.Equal(
            MonotonicTimestamp.FromElapsed(TimeSpan.MaxValue),
            clock.RequestedDeadline);
    }

    [Fact]
    public async Task CountdownPropagatesCancellationFromMonotonicDelay()
    {
        var clock = new TrackingMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero),
            waitForCancellation: true);
        var countdown = new MonotonicCountdownTimer(clock);
        using var cancellation = new CancellationTokenSource();

        var wait = countdown.WaitAsync(
            SelfTimer.FromSeconds(3),
            cancellation.Token);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => wait);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private sealed class TrackingMonotonicClock : IMonotonicClock
    {
        private readonly MonotonicTimestamp _now;
        private readonly bool _waitForCancellation;

        public TrackingMonotonicClock(
            MonotonicTimestamp now,
            bool waitForCancellation = false)
        {
            _now = now;
            _waitForCancellation = waitForCancellation;
        }

        public int NowReadCount { get; private set; }

        public int DelayCallCount { get; private set; }

        public MonotonicTimestamp? RequestedDeadline { get; private set; }

        public CancellationToken RequestedCancellationToken { get; private set; }

        public MonotonicTimestamp Now
        {
            get
            {
                NowReadCount++;
                return _now;
            }
        }

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken)
        {
            DelayCallCount++;
            RequestedDeadline = deadline;
            RequestedCancellationToken = cancellationToken;
            return _waitForCancellation
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(
            DateTimeOffset utcNow,
            TimeZoneInfo? localTimeZone = null)
        {
            _utcNow = utcNow.ToUniversalTime();
            LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
        }

        public override TimeZoneInfo LocalTimeZone { get; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public int TimerCreationCount { get; private set; }

        public TimeSpan LastDueTime { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            lock (_sync)
            {
                TimerCreationCount++;
                LastDueTime = dueTime;
                var timer = new ManualTimer(
                    this,
                    callback,
                    state,
                    dueTime,
                    period);
                _timers.Add(timer);
                return timer;
            }
        }

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                duration,
                TimeSpan.Zero);

            List<ManualTimer> dueTimers;
            lock (_sync)
            {
                _timestamp = checked(_timestamp + duration.Ticks);
                _utcNow += duration;
                dueTimers = _timers
                    .Where(timer => timer.IsDue(_timestamp))
                    .ToList();
            }

            foreach (var timer in dueTimers)
            {
                timer.Fire();
            }
        }

        public void SetTimestamp(long timestamp)
        {
            lock (_sync)
            {
                _timestamp = timestamp;
            }
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            lock (_sync)
            {
                _utcNow = utcNow.ToUniversalTime();
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_sync)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _disposed;
            private long? _dueAtTimestamp;
            private TimeSpan _period;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAtTimestamp = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : checked(_owner.GetTimestamp() + dueTime.Ticks);
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(long timestamp) =>
                !_disposed &&
                _dueAtTimestamp is { } dueAt &&
                timestamp >= dueAt;

            public void Fire()
            {
                if (_disposed)
                {
                    return;
                }

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _dueAtTimestamp = null;
                }
                else
                {
                    _dueAtTimestamp = checked(
                        _owner.GetTimestamp() + _period.Ticks);
                }

                _callback(_state);
            }
        }
    }
}
