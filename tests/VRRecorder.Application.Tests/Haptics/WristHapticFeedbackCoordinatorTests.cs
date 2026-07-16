using VRRecorder.Application.Haptics;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Haptics;

public sealed class WristHapticFeedbackCoordinatorTests
{
    [Theory]
    [InlineData(WristHapticFeedbackKind.RecordingStarted, 30, 1)]
    [InlineData(WristHapticFeedbackKind.RecordingStopped, 20, 2)]
    [InlineData(WristHapticFeedbackKind.Fault, 80, 1)]
    public async Task RecordingTransitionProducesSpecifiedPulsePattern(
        WristHapticFeedbackKind kind,
        int durationMilliseconds,
        int pulseCount)
    {
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());

        var result = await coordinator.PublishAsync(
            revision: 7,
            kind,
            CancellationToken.None);

        var delivered = Assert.IsType<
            WristHapticFeedbackResult.Delivered>(result);
        Assert.Equal(7, delivered.Revision);
        var pattern = Assert.Single(output.Patterns);
        Assert.Equal(
            TimeSpan.FromMilliseconds(durationMilliseconds),
            pattern.Duration);
        Assert.Equal(pulseCount, pattern.PulseCount);
        Assert.Equal(120f, pattern.FrequencyHertz);
        Assert.Equal(0.65f, pattern.Amplitude);
    }

    [Fact]
    public void PulsePatternRejectsInvalidRuntimeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticPattern(
                TimeSpan.Zero,
                pulseCount: 1,
                frequencyHertz: 120,
                amplitude: 0.65f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticPattern(
                TimeSpan.FromMilliseconds(30),
                pulseCount: 0,
                frequencyHertz: 120,
                amplitude: 0.65f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticPattern(
                TimeSpan.FromMilliseconds(30),
                pulseCount: 1,
                frequencyHertz: float.NaN,
                amplitude: 0.65f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticPattern(
                TimeSpan.FromMilliseconds(30),
                pulseCount: 1,
                frequencyHertz: 120,
                amplitude: 1.01f));
    }

    [Fact]
    public void FeedbackOptionsRejectInvalidRuntimeValuesAtComposition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticFeedbackOptions(
                isEnabled: true,
                frequencyHertz: -1,
                amplitude: 0.65f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WristHapticFeedbackOptions(
                isEnabled: true,
                frequencyHertz: 120,
                amplitude: 0));
    }

    [Fact]
    public async Task DisabledFeedbackConsumesRevisionWithoutCallingOutput()
    {
        var output = new TrackingOutput();
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            new WristHapticFeedbackOptions(
                isEnabled: false,
                frequencyHertz: 120,
                amplitude: 0.65f));

        var first = await coordinator.PublishAsync(
            revision: 8,
            WristHapticFeedbackKind.RecordingStarted,
            CancellationToken.None);
        var duplicate = await coordinator.PublishAsync(
            revision: 8,
            WristHapticFeedbackKind.RecordingStarted,
            CancellationToken.None);

        Assert.IsType<WristHapticFeedbackResult.Disabled>(first);
        Assert.IsType<WristHapticFeedbackResult.Ignored>(duplicate);
        Assert.Empty(output.Patterns);
    }

    [Fact]
    public async Task OutputFailureIsReportedWithoutThrowingOrRetryingRevision()
    {
        var failure = new InvalidOperationException("controller disconnected");
        var output = new TrackingOutput { Failure = failure };
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());

        var result = await coordinator.PublishAsync(
            revision: 9,
            WristHapticFeedbackKind.Fault,
            CancellationToken.None);
        output.Failure = null;
        var duplicate = await coordinator.PublishAsync(
            revision: 9,
            WristHapticFeedbackKind.Fault,
            CancellationToken.None);

        var failed = Assert.IsType<WristHapticFeedbackResult.Failed>(result);
        Assert.Equal(9, failed.Revision);
        Assert.Same(failure, failed.Failure);
        Assert.IsType<WristHapticFeedbackResult.Ignored>(duplicate);
        Assert.Single(output.Patterns);
    }

    [Fact]
    public async Task LaterRevisionCanDeliverAfterEarlierOutputFailure()
    {
        var output = new TrackingOutput
        {
            Failure = new InvalidOperationException("runtime restart"),
        };
        var coordinator = new WristHapticFeedbackCoordinator(
            output,
            EnabledOptions());

        await coordinator.PublishAsync(
            revision: 10,
            WristHapticFeedbackKind.Fault,
            CancellationToken.None);
        output.Failure = null;
        var result = await coordinator.PublishAsync(
            revision: 11,
            WristHapticFeedbackKind.RecordingStarted,
            CancellationToken.None);

        Assert.IsType<WristHapticFeedbackResult.Delivered>(result);
        Assert.Equal(2, output.Patterns.Count);
    }

    private static WristHapticFeedbackOptions EnabledOptions() =>
        new(
            isEnabled: true,
            frequencyHertz: 120,
            amplitude: 0.65f);

    private sealed class TrackingOutput : IWristHapticOutput
    {
        public List<WristHapticPattern> Patterns { get; } = [];

        public Exception? Failure { get; set; }

        public Task PlayAsync(
            WristHapticPattern pattern,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Patterns.Add(pattern);
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }
    }
}
