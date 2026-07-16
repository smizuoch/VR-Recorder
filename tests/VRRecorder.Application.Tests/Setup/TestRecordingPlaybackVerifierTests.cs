using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Setup;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Setup;

public sealed class TestRecordingPlaybackVerifierTests
{
    [Fact]
    public async Task RecordsForRequestedDurationThenLaunchesFinalizedFile()
    {
        var runtime = new CapturingRuntime(publishSavedOnStop: true);
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10)));
        var launcher = new CapturingLauncher(started: true);
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            clock,
            launcher);

        var verification = verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var deadline = await clock.WaitUntilDeadlineRequestedAsync();

        Assert.Equal(TimeSpan.FromSeconds(13), deadline.Elapsed);
        Assert.Equal(1, runtime.StartCount);
        Assert.Equal(RecorderState.Recording, runtime.Current.State);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(3200));
        var evidence = await verification;

        Assert.NotNull(evidence);
        Assert.Equal(TimeSpan.FromMilliseconds(3200), evidence.Duration);
        Assert.True(evidence.IsFinalized);
        Assert.True(evidence.HasVideoStream);
        Assert.True(evidence.HasAudioStream);
        Assert.True(evidence.PlaybackStarted);
        Assert.Equal(1, runtime.StopCount);
        Assert.Equal(runtime.SavedRecording, launcher.Recording);
    }

    [Fact]
    public async Task MissingSavedNotificationDoesNotClaimFinalization()
    {
        var runtime = new CapturingRuntime(publishSavedOnStop: false);
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            clock,
            new CapturingLauncher(started: true));

        var verification = verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        await clock.WaitUntilDeadlineRequestedAsync();
        clock.AdvanceBy(TimeSpan.FromSeconds(3));

        Assert.Null(await verification);
    }

    [Fact]
    public async Task PlaybackLaunchFailureReturnsExplicitNegativeEvidence()
    {
        var runtime = new CapturingRuntime(publishSavedOnStop: true);
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            clock,
            new CapturingLauncher(started: false));

        var verification = verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        await clock.WaitUntilDeadlineRequestedAsync();
        clock.AdvanceBy(TimeSpan.FromSeconds(3));
        var evidence = await verification;

        Assert.NotNull(evidence);
        Assert.False(evidence.PlaybackStarted);
    }

    [Fact]
    public async Task CancellationDuringCaptureStopsBestEffort()
    {
        var runtime = new CapturingRuntime(publishSavedOnStop: true);
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            clock,
            new CapturingLauncher(started: true));
        using var cancellation = new CancellationTokenSource();

        var verification = verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            cancellation.Token);
        await clock.WaitUntilDeadlineRequestedAsync();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verification);
        Assert.Equal(1, runtime.StopCount);
        Assert.Equal(RecorderState.Ready, runtime.Current.State);
    }

    [Fact]
    public async Task CancellationBeforeRecordingStateStillRequestsStop()
    {
        var runtime = new CancelingStartRuntime();
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            new ControllableMonotonicClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingLauncher(started: true));
        using var cancellation = new CancellationTokenSource();

        var verification = verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            cancellation.Token);
        await runtime.StartEntered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verification);
        Assert.Equal(1, runtime.StopCount);
    }

    [Fact]
    public async Task NonReadyRuntimeDoesNotStartOrLaunch()
    {
        var runtime = new CapturingRuntime(
            publishSavedOnStop: true,
            initialState: RecorderState.Faulted);
        var launcher = new CapturingLauncher(started: true);
        var verifier = new TestRecordingPlaybackVerifier(
            runtime,
            new ControllableMonotonicClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            launcher);

        var evidence = await verifier.VerifyAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Null(evidence);
        Assert.Equal(0, runtime.StartCount);
        Assert.Null(launcher.Recording);
    }

    private sealed class CapturingRuntime
        : ITestRecordingPlaybackRuntime
    {
        private readonly bool _publishSavedOnStop;
        private readonly List<Action<FinalizedRecording>> _saved = [];

        public CapturingRuntime(
            bool publishSavedOnStop,
            RecorderState initialState = RecorderState.Ready)
        {
            _publishSavedOnStop = publishSavedOnStop;
            Current = RecorderStatusSnapshot.Create(0, initialState);
            SavedRecording = new FinalizedRecording(
                Path.GetFullPath("first-run-test.mp4"));
        }

        public RecorderStatusSnapshot Current { get; private set; }

        public FinalizedRecording SavedRecording { get; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            Current = RecorderStatusSnapshot.Create(
                Current.Revision + 1,
                RecorderState.Recording);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (Current.State == RecorderState.Ready)
            {
                return Task.CompletedTask;
            }

            StopCount++;
            Current = RecorderStatusSnapshot.Create(
                Current.Revision + 1,
                RecorderState.Ready);
            if (_publishSavedOnStop)
            {
                foreach (var subscriber in _saved.ToArray())
                {
                    subscriber(SavedRecording);
                }
            }
            return Task.CompletedTask;
        }

        public IDisposable SubscribeSaved(
            Action<FinalizedRecording> subscriber)
        {
            _saved.Add(subscriber);
            return new CallbackDisposable(() => _saved.Remove(subscriber));
        }
    }

    private sealed class CapturingLauncher(bool started)
        : IRecordingPlaybackLauncher
    {
        public FinalizedRecording? Recording { get; private set; }

        public Task<bool> StartAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recording = recording;
            return Task.FromResult(started);
        }
    }

    private sealed class CancelingStartRuntime
        : ITestRecordingPlaybackRuntime
    {
        public TaskCompletionSource StartEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecorderStatusSnapshot Current { get; } =
            RecorderStatusSnapshot.Create(0, RecorderState.Ready);

        public int StopCount { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartEntered.TrySetResult();
            await Task
                .Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public IDisposable SubscribeSaved(
            Action<FinalizedRecording> subscriber) =>
            new CallbackDisposable(static () => { });
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() =>
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
