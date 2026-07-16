using System.Threading.Channels;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristOverlayBackgroundHostTests
{
    [Fact]
    public async Task TicksImmediatelyAndCoalescesToTheLatestStatusAtNinetyHertz()
    {
        var statuses = new FakeStatusSource(Status(1));
        var ticker = new CapturingTicker();
        var clock = new ControllableClock();
        var host = new WristOverlayBackgroundHost(
            statuses,
            new WristUiProjector(EnglishUiLocalizer.Instance),
            ticker,
            clock);
        using var cancellation = new CancellationTokenSource();

        var run = host.RunAsync(cancellation.Token);
        var first = await ticker.NextAsync();
        var firstDeadline = await clock.NextDeadlineAsync();
        Assert.Equal(1, first.Revision);
        Assert.Equal(
            WristOverlayBackgroundHost.PollInterval,
            firstDeadline.Elapsed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.RunAsync(CancellationToken.None));

        statuses.Publish(Status(2));
        statuses.Publish(Status(3));
        clock.AdvanceTo(firstDeadline);
        var second = await ticker.NextAsync();
        Assert.Equal(3, second.Revision);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, statuses.ActiveSubscriptions);
    }

    [Fact]
    public async Task ReschedulesFromNowWhenATickMissesItsDeadline()
    {
        var statuses = new FakeStatusSource(Status(4));
        var clock = new ControllableClock();
        var ticker = new CapturingTicker
        {
            BeforeReturn = () =>
                clock.AdvanceTo(MonotonicTimestamp.FromElapsed(
                    TimeSpan.FromMilliseconds(100))),
        };
        var host = new WristOverlayBackgroundHost(
            statuses,
            new WristUiProjector(EnglishUiLocalizer.Instance),
            ticker,
            clock);
        using var cancellation = new CancellationTokenSource();

        var run = host.RunAsync(cancellation.Token);
        await ticker.NextAsync();
        var deadline = await clock.NextDeadlineAsync();

        Assert.Equal(
            TimeSpan.FromMilliseconds(100) +
            WristOverlayBackgroundHost.PollInterval,
            deadline.Elapsed);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, statuses.ActiveSubscriptions);
    }

    [Fact]
    public async Task PropagatesTickFailureAndUnsubscribes()
    {
        var statuses = new FakeStatusSource(Status(5));
        var expected = new InvalidOperationException("texture failed");
        var ticker = new CapturingTicker { Failure = expected };
        var host = new WristOverlayBackgroundHost(
            statuses,
            new WristUiProjector(EnglishUiLocalizer.Instance),
            ticker,
            new ControllableClock());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.RunAsync(CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(0, statuses.ActiveSubscriptions);
    }

    private static RecorderStatusSnapshot Status(long revision) => new(
        revision,
        RecorderState.Ready,
        RecorderAvailableActions.Start);

    private sealed class CapturingTicker : IWristOverlayInteractionTicker
    {
        private readonly Channel<WristUiSnapshot> _ticks =
            Channel.CreateUnbounded<WristUiSnapshot>();

        public Action? BeforeReturn { get; init; }

        public Exception? Failure { get; init; }

        public Task<WristOverlayInteractionTickResult> TickAsync(
            WristUiSnapshot snapshot,
            TimeSpan now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<WristOverlayInteractionTickResult>(
                    Failure);
            }
            _ticks.Writer.TryWrite(snapshot);
            BeforeReturn?.Invoke();
            return Task.FromResult(new WristOverlayInteractionTickResult(
                new WristTextureHostTickResult(
                    Published: true,
                    BecameVisible: true,
                    WristTextureUpdateReason.InitialFrame),
                PointerEventsPolled: 0,
                ActionDispatched: false));
        }

        public async Task<WristUiSnapshot> NextAsync() =>
            await _ticks.Reader.ReadAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));
    }

    private sealed class ControllableClock : IMonotonicClock
    {
        private readonly object _gate = new();
        private readonly Channel<MonotonicTimestamp> _deadlines =
            Channel.CreateUnbounded<MonotonicTimestamp>();
        private readonly List<(
            MonotonicTimestamp Deadline,
            TaskCompletionSource Completion)> _delays = [];
        private MonotonicTimestamp _now =
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public MonotonicTimestamp Now
        {
            get
            {
                lock (_gate)
                {
                    return _now;
                }
            }
        }

        public async Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource completion;
            lock (_gate)
            {
                if (deadline.Elapsed <= _now.Elapsed)
                {
                    return;
                }
                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _delays.Add((deadline, completion));
            }
            _deadlines.Writer.TryWrite(deadline);
            await completion.Task.WaitAsync(cancellationToken);
        }

        public async Task<MonotonicTimestamp> NextDeadlineAsync() =>
            await _deadlines.Reader.ReadAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));

        public void AdvanceTo(MonotonicTimestamp now)
        {
            TaskCompletionSource[] completed;
            lock (_gate)
            {
                if (now.Elapsed < _now.Elapsed)
                {
                    throw new ArgumentOutOfRangeException(nameof(now));
                }
                _now = now;
                completed = _delays
                    .Where(delay => delay.Deadline.Elapsed <= now.Elapsed)
                    .Select(delay => delay.Completion)
                    .ToArray();
                _delays.RemoveAll(delay =>
                    delay.Deadline.Elapsed <= now.Elapsed);
            }
            foreach (var completion in completed)
            {
                completion.TrySetResult();
            }
        }
    }

    private sealed class FakeStatusSource : IRecorderStatusSource
    {
        private readonly object _gate = new();
        private RecorderStatusSnapshot _current;
        private Action<RecorderStatusSnapshot>? _subscriber;

        public FakeStatusSource(RecorderStatusSnapshot initial)
        {
            _current = initial;
        }

        public RecorderStatusSnapshot Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public int ActiveSubscriptions
        {
            get
            {
                lock (_gate)
                {
                    return _subscriber is null ? 0 : 1;
                }
            }
        }

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            lock (_gate)
            {
                if (_subscriber is not null)
                {
                    throw new InvalidOperationException(
                        "Only one test subscriber is supported.");
                }
                _subscriber = subscriber;
            }
            return new Subscription(this);
        }

        public void Publish(RecorderStatusSnapshot status)
        {
            Action<RecorderStatusSnapshot>? subscriber;
            lock (_gate)
            {
                _current = status;
                subscriber = _subscriber;
            }
            subscriber?.Invoke(status);
        }

        private void Unsubscribe()
        {
            lock (_gate)
            {
                _subscriber = null;
            }
        }

        private sealed class Subscription(FakeStatusSource owner)
            : IDisposable
        {
            private FakeStatusSource? _owner = owner;

            public void Dispose() =>
                Interlocked.Exchange(ref _owner, null)?.Unsubscribe();
        }
    }
}
