using System.Diagnostics;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Recording;

public sealed class RecordingLifecycleIntegrationTests
{
    private const string FfmpegPath = "/usr/bin/ffmpeg";
    private const string FfprobePath = "/usr/bin/ffprobe";

    [Fact]
    [Trait("Scenario", "IT-019")]
    public async Task DiskLowSafelyStopsAndPublishesOnePlayableRecording()
    {
        Assert.True(File.Exists(FfmpegPath), $"Missing host tool: {FfmpegPath}");
        Assert.True(File.Exists(FfprobePath), $"Missing host tool: {FfprobePath}");
        using var directory = TemporaryDirectory.Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var clock = new ControllableMonotonicClock();
        var backend = new SyntheticNativeRecordingBackend(FfmpegPath);
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new UnexpectedRuntimeFaultSink());
        var savedRecordings = new CapturingSavedRecordingSink();
        var finalization = new RecordingFileFinalizationUseCase(
            new SameDirectoryAtomicRecordingFileFinalizer(),
            new FfprobeRecordingFileValidator(
                FfprobePath,
                new RecordingMediaExpectation(
                    Width: 320,
                    Height: 180,
                    FramesPerSecond: 30,
                    AudioSampleRate: 48000,
                    AudioChannels: 2,
                    ExpectedDuration: TimeSpan.FromSeconds(3))),
            new FileSystemRecordingRecoveryStore(),
            savedRecordings);
        var lifecycle = new ActiveRecordingSessionCoordinator(
            engine,
            finalization);
        var storageProbe = new SequencedStorageSpaceProbe(
            new StorageSpace(StorageCapacityPolicy.MinimumStartBytes),
            new StorageSpace(StorageCapacityPolicy.StopBelowBytes - 1));
        var storageStatus = new CapturingStorageStatusSink();
        var storageMonitor = new RecordingStorageMonitor(
            estimatedBytesPerSecond: 1_000_000,
            clock,
            storageProbe,
            storageStatus,
            lifecycle);
        var useCase = new StartRecordingUseCase(
            new StableVideoSignalGateway(new StableVideoSignal(320, 180)),
            new ImmediateCountdownTimer(),
            new FileSystemRecordingFileReservation(),
            new FixedWallClock(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            storageProbe,
            engine,
            lifecycle,
            storageMonitor,
            new AutoStopScheduler(clock, lifecycle));

        var result = await useCase.ExecuteAsync(
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(directory.Path),
                new FrameRate(30)),
            timeout.Token);

        var started = Assert.IsType<StartRecordingResult.Started>(result);
        var plan = Assert.IsType<RecordingPlan>(backend.Plan);
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.True(File.Exists(plan.Output.TemporaryPath));
        Assert.False(File.Exists(plan.Output.FinalPath));
        Assert.Empty(savedRecordings.Recordings);
        var deadline = await clock.WaitUntilDelayRequestedAsync();
        Assert.Equal(
            started.Handle.FirstPacketCommittedAt.Add(
                StorageCapacityPolicy.MonitorInterval),
            deadline);

        clock.CompleteDelay();
        await backend.Session.WaitUntilStopRequestedAsync();

        Assert.Equal(1, backend.Session.StopCallCount);
        Assert.Equal(RecordingStopReason.DiskLow, lifecycle.StopReason);
        Assert.Equal(RecorderState.Stopping, lifecycle.State);
        Assert.False(started.StorageMonitoringCompletion.IsCompleted);
        Assert.True(File.Exists(plan.Output.TemporaryPath));
        Assert.False(File.Exists(plan.Output.FinalPath));
        Assert.Empty(savedRecordings.Recordings);

        backend.Session.CompleteStop();
        await started.StorageMonitoringCompletion;

        Assert.Equal(2, storageProbe.CallCount);
        var snapshot = Assert.Single(storageStatus.Snapshots);
        Assert.Equal(RecordingStorageState.StopRequired, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.EstimatedRemaining);
        Assert.False(File.Exists(plan.Output.TemporaryPath));
        Assert.True(File.Exists(plan.Output.FinalPath));
        Assert.Equal(
            Path.GetFullPath(plan.Output.FinalPath),
            Assert.Single(savedRecordings.Recordings).FinalPath);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(1, backend.Session.StopCallCount);
    }

    private sealed class StableVideoSignalGateway(StableVideoSignal signal)
        : IVideoSignalGateway
    {
        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(signal);
        }
    }

    private sealed class ImmediateCountdownTimer : ICountdownTimer
    {
        public Task WaitAsync(
            SelfTimer timer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
    }

    private sealed class SequencedStorageSpaceProbe(params StorageSpace[] values)
        : IStorageSpaceProbe
    {
        public int CallCount { get; private set; }

        public Task<StorageSpace> MeasureAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = CallCount++;
            if (index >= values.Length)
            {
                throw new InvalidOperationException(
                    "The storage probe was called more often than expected.");
            }

            return Task.FromResult(values[index]);
        }
    }

    private sealed class CapturingStorageStatusSink
        : IRecordingStorageStatusSink
    {
        public List<RecordingStorageSnapshot> Snapshots { get; } = [];

        public Task PublishAsync(
            RecordingStorageSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableMonotonicClock : IMonotonicClock
    {
        private readonly TaskCompletionSource<MonotonicTimestamp> _delayRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _delayCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MonotonicTimestamp Now { get; } =
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken)
        {
            _delayRequested.TrySetResult(deadline);
            return _delayCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<MonotonicTimestamp> WaitUntilDelayRequestedAsync() =>
            _delayRequested.Task;

        public void CompleteDelay() => _delayCompletion.TrySetResult();
    }

    private sealed class SyntheticNativeRecordingBackend(string ffmpegPath)
        : INativeRecordingBackend
    {
        public RecordingPlan? Plan { get; private set; }

        public SyntheticNativeRecordingSession Session { get; private set; } = null!;

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            NativeRecordingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Plan = plan;
            Session = new SyntheticNativeRecordingSession(ffmpegPath, plan.Output);
            callbacks.FirstVideoPacketMuxed();
            return Task.FromResult<INativeRecordingSession>(Session);
        }
    }

    private sealed class SyntheticNativeRecordingSession(
        string ffmpegPath,
        PendingRecording output) : INativeRecordingSession
    {
        private readonly TaskCompletionSource _stopRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopAllowed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "synthetic-native-session-001";

        public int StopCallCount { get; private set; }

        public Task AbortAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Abort was not expected.");

        public async Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            _stopRequested.TrySetResult();
            await _stopAllowed.Task.WaitAsync(cancellationToken);
            await GenerateSyntheticRecordingAsync(
                ffmpegPath,
                output.TemporaryPath,
                cancellationToken);
            return new RecordingStopResult(
                output,
                VideoPacketCount: 90,
                AudioPacketCount: 142);
        }

        public Task WaitUntilStopRequestedAsync() => _stopRequested.Task;

        public void CompleteStop() => _stopAllowed.TrySetResult();
    }

    private sealed class CapturingSavedRecordingSink : ISavedRecordingSink
    {
        public List<FinalizedRecording> Recordings { get; } = [];

        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recordings.Add(recording);
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedRuntimeFaultSink
        : INativeRecordingRuntimeFaultSink
    {
        public void Report(NativeRecordingFault fault) =>
            throw new InvalidOperationException(
                $"Unexpected native runtime fault: {fault.Message}");
    }

    private static async Task GenerateSyntheticRecordingAsync(
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
            "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high",
            "-pix_fmt", "yuv420p", "-g", "60", "-keyint_min", "60",
            "-sc_threshold", "0", "-bf", "0", "-threads:v", "1",
            "-fps_mode", "cfr", "-c:a", "aac", "-profile:a", "aac_low",
            "-b:a", "192k", "-ar", "48000", "-ac", "2", "-t", "3",
            "-shortest", "-map_metadata", "-1", "-metadata",
            "creation_time=1970-01-01T00:00:00Z", "-movflags",
            "+frag_keyframe+empty_moov+default_base_moof", "-frag_duration",
            "1000000", "-flush_packets", "1", "-f", "mp4", outputPath,
        ];
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException(
                                "Could not start ffmpeg.");
        var standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg failed with {process.ExitCode}: {await standardError}");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-lifecycle-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
