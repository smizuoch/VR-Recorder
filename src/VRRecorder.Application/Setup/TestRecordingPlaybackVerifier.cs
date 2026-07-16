using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Setup;

public sealed class TestRecordingPlaybackVerifier
    : ITestRecordingPlaybackVerifier
{
    private readonly ITestRecordingPlaybackRuntime _runtime;
    private readonly IMonotonicClock _clock;
    private readonly IRecordingPlaybackLauncher _playback;
    private int _verificationActive;

    public TestRecordingPlaybackVerifier(
        ITestRecordingPlaybackRuntime runtime,
        IMonotonicClock clock,
        IRecordingPlaybackLauncher playback)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(playback);
        _runtime = runtime;
        _clock = clock;
        _playback = playback;
    }

    public async Task<TestRecordingPlaybackEvidence?> VerifyAsync(
        TimeSpan requestedDuration,
        CancellationToken cancellationToken)
    {
        if (requestedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedDuration),
                requestedDuration,
                "The first-run test recording duration must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(
                ref _verificationActive,
                1,
                0) != 0)
        {
            return null;
        }

        try
        {
            return await VerifyCoreAsync(
                    requestedDuration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _verificationActive, 0);
        }
    }

    private async Task<TestRecordingPlaybackEvidence?> VerifyCoreAsync(
        TimeSpan requestedDuration,
        CancellationToken cancellationToken)
    {
        if (_runtime.Current.State != RecorderState.Ready)
        {
            return null;
        }

        var savedGate = new object();
        FinalizedRecording? saved = null;
        using var subscription = _runtime.SubscribeSaved(recording =>
        {
            ArgumentNullException.ThrowIfNull(recording);
            lock (savedGate)
            {
                saved ??= recording;
            }
        });

        var startRequested = false;
        try
        {
            startRequested = true;
            await _runtime
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);
            if (_runtime.Current.State != RecorderState.Recording)
            {
                return null;
            }

            var recordingStarted = _clock.Now;
            await _clock
                .DelayUntilAsync(
                    recordingStarted.Add(requestedDuration),
                    cancellationToken)
                .ConfigureAwait(false);
            var recordedDuration =
                _clock.Now.Elapsed - recordingStarted.Elapsed;

            await _runtime
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
            if (_runtime.Current.State != RecorderState.Ready)
            {
                return null;
            }

            FinalizedRecording? finalized;
            lock (savedGate)
            {
                finalized = saved;
            }
            if (finalized is null)
            {
                return null;
            }

            var playbackStarted = await _playback
                .StartAsync(finalized, cancellationToken)
                .ConfigureAwait(false);
            return new TestRecordingPlaybackEvidence(
                recordedDuration,
                IsFinalized: true,
                HasVideoStream: true,
                HasAudioStream: true,
                playbackStarted);
        }
        finally
        {
            if (startRequested)
            {
                try
                {
                    await _runtime
                        .StopAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "First-run test recording cleanup failed: {0}",
                        exception.GetType().Name);
                }
            }
        }
    }
}
