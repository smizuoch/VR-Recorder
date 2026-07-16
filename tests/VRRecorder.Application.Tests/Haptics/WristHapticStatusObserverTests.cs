using VRRecorder.Application.Haptics;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Haptics;

public sealed class WristHapticStatusObserverTests
{
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
