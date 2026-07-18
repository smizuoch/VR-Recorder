using VRRecorder.Application.Haptics;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Haptics;

public sealed class WristHapticStatusObserverTests
{
    [Fact]
    public void NullStatusSourceIsRejectedImmediately()
    {
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());

        Assert.Throws<ArgumentNullException>(() =>
            new WristHapticStatusObserver(null!, coordinator));
    }

    [Fact]
    public async Task NullFeedbackCoordinatorIsRejectedImmediately()
    {
        using var statuses = Statuses(RecorderState.Ready);
        WristHapticStatusObserver? unexpectedObserver = null;

        try
        {
            Assert.Throws<ArgumentNullException>(() =>
                unexpectedObserver = new WristHapticStatusObserver(
                    statuses,
                    null!));
        }
        finally
        {
            if (unexpectedObserver is not null)
            {
                await unexpectedObserver.DisposeAsync();
            }
        }
    }

    [Fact]
    public void SubscribeFailureIsPropagatedFromConstruction()
    {
        var failure = new InvalidOperationException("subscribe failed");
        var statuses = new ThrowingStatusSource(failure);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());

        var observed = Record.Exception(() =>
            new WristHapticStatusObserver(statuses, coordinator));

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task DisposeUnsubscribesExactlyOnce()
    {
        var statuses = new TrackingStatusSource(RecorderState.Ready);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        await observer.DisposeAsync();
        await observer.DisposeAsync();

        Assert.Equal(1, statuses.SubscriptionDisposeCount);
    }

    [Fact]
    public async Task NullPublishedStatusIsRejectedSynchronously()
    {
        var statuses = new TrackingStatusSource(RecorderState.Ready);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Assert.Throws<ArgumentNullException>(() => statuses.Publish(null!));
    }

    [Fact]
    public async Task SuccessfulRecordingLifecycleEmitsStartThenStop()
    {
        using var statuses = Statuses(RecorderState.Ready);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Publish(statuses, 1, RecorderState.Recording);
        Publish(statuses, 2, RecorderState.Stopping);
        Publish(statuses, 3, RecorderState.Ready);
        await observer.DisposeAsync();

        Assert.Equal(
            [
                (TimeSpan.FromMilliseconds(30), 1),
                (TimeSpan.FromMilliseconds(20), 2),
            ],
            output.Patterns.Select(pattern =>
                (pattern.Duration, pattern.PulseCount)));
    }

    [Fact]
    public async Task InitialRecordingStateEmitsOnlyStopWhenReturningToReady()
    {
        using var statuses = Statuses(RecorderState.Recording);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Publish(statuses, 1, RecorderState.Ready);
        await observer.DisposeAsync();

        var pattern = Assert.Single(output.Patterns);
        Assert.Equal(TimeSpan.FromMilliseconds(20), pattern.Duration);
        Assert.Equal(2, pattern.PulseCount);
    }

    [Theory]
    [InlineData(RecorderState.SignalLost)]
    [InlineData(RecorderState.NoSignal)]
    [InlineData(RecorderState.Faulted)]
    public async Task SignalOrFaultTransitionEmitsFaultPulse(
        RecorderState state)
    {
        using var statuses = Statuses(RecorderState.Ready);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Publish(statuses, 1, state);
        await observer.DisposeAsync();

        var pattern = Assert.Single(output.Patterns);
        Assert.Equal(TimeSpan.FromMilliseconds(80), pattern.Duration);
        Assert.Equal(1, pattern.PulseCount);
    }

    [Theory]
    [InlineData(RecorderState.Faulted)]
    [InlineData(RecorderState.ComplianceFault)]
    public async Task HardFaultIsNotRepeatedAndClearsRecordingLifecycle(
        RecorderState state)
    {
        using var statuses = Statuses(RecorderState.Recording);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Publish(statuses, 1, state);
        Publish(statuses, 2, state);
        Publish(statuses, 3, RecorderState.Ready);
        await observer.DisposeAsync();

        var pattern = Assert.Single(output.Patterns);
        Assert.Equal(TimeSpan.FromMilliseconds(80), pattern.Duration);
        Assert.Equal(1, pattern.PulseCount);
    }

    [Fact]
    public async Task NoSignalIsNotRepeatedAndClearsRecordingLifecycle()
    {
        using var statuses = Statuses(RecorderState.Recording);
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator);

        Publish(statuses, 1, RecorderState.NoSignal);
        Publish(statuses, 2, RecorderState.NoSignal);
        Publish(statuses, 3, RecorderState.Ready);
        await observer.DisposeAsync();

        var pattern = Assert.Single(output.Patterns);
        Assert.Equal(TimeSpan.FromMilliseconds(80), pattern.Duration);
        Assert.Equal(1, pattern.PulseCount);
    }

    [Fact]
    public async Task OutputFailureDoesNotBlockLaterTransition()
    {
        using var statuses = Statuses(RecorderState.Ready);
        var output = new TrackingOutput { FailNext = true };
        var failures = new List<Exception>();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());
        await using var observer = new WristHapticStatusObserver(
            statuses,
            coordinator,
            failures.Add);

        Publish(statuses, 1, RecorderState.NoSignal);
        Publish(statuses, 2, RecorderState.Ready);
        Publish(statuses, 3, RecorderState.Recording);
        await observer.DisposeAsync();

        Assert.Equal(2, output.Patterns.Count);
        Assert.Single(failures);
        Assert.Equal("controller disconnected", failures[0].Message);
    }

    private static RecorderStatusHub Statuses(RecorderState state) =>
        new(RecorderStatusSnapshot.Create(0, state));

    private static void Publish(
        RecorderStatusHub statuses,
        long revision,
        RecorderState state) =>
        Assert.True(statuses.TryPublish(
            RecorderStatusSnapshot.Create(revision, state)));

    private static WristHapticFeedbackOptions EnabledOptions() =>
        new(
            isEnabled: true,
            frequencyHertz: 120,
            amplitude: 0.65f);

    private sealed class ThrowingStatusSource(Exception failure)
        : IRecorderStatusSource
    {
        public RecorderStatusSnapshot Current =>
            RecorderStatusSnapshot.Create(0, RecorderState.Ready);

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            throw failure;
    }

    private sealed class TrackingStatusSource(
        RecorderState initialState) : IRecorderStatusSource
    {
        private Action<RecorderStatusSnapshot>? _subscriber;

        public RecorderStatusSnapshot Current { get; } =
            RecorderStatusSnapshot.Create(0, initialState);

        public int SubscriptionDisposeCount { get; private set; }

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber)
        {
            _subscriber = subscriber;
            subscriber(Current);
            return new CallbackDisposable(() =>
            {
                SubscriptionDisposeCount++;
                _subscriber = null;
            });
        }

        public void Publish(RecorderStatusSnapshot status) =>
            (_subscriber ?? throw new InvalidOperationException(
                "No status subscriber is active."))(status);
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose() =>
            Interlocked.Exchange(ref _callback, null)?.Invoke();
    }

    private sealed class TrackingOutput : IWristHapticOutput
    {
        public List<WristHapticPattern> Patterns { get; } = [];

        public bool FailNext { get; set; }

        public Task PlayAsync(
            WristHapticPattern pattern,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Patterns.Add(pattern);
            if (!FailNext)
            {
                return Task.CompletedTask;
            }

            FailNext = false;
            return Task.FromException(
                new InvalidOperationException("controller disconnected"));
        }
    }
}
