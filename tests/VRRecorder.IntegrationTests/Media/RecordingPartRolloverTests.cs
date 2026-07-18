using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class RecordingPartRolloverTests
{
    [Fact]
    public async Task ReservesSoftwarePartAndFinalizesTheSealedPredecessor()
    {
        var directory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "vrrecorder-rollover"));
        var first = new PendingRecording(
            Path.Combine(directory, "take.recording.mp4"),
            Path.Combine(directory, "take.mp4"));
        var second = new PendingRecording(
            Path.Combine(directory, "take_part002.recording.mp4"),
            Path.Combine(directory, "take_part002.mp4"));
        var reservation = new CapturingReservation(second);
        var finalizer = new CapturingFinalizer();
        var validator = new CapturingValidator();
        var saved = new CapturingSavedRecordingSink();
        var rollover = new RecordingPartRollover(
            reservation,
            new RecordingFileFinalizationUseCase(
                finalizer,
                validator,
                new UnexpectedRecoveryStore(),
                saved));
        var startedAt = new RecordingSessionTimestamp(new DateTimeOffset(
            2026,
            7,
            17,
            21,
            15,
            0,
            TimeSpan.Zero));
        var plan = new RecordingPlan(
            new StableVideoSignal(1_920, 1_080),
            first,
            startedAt,
            new FrameRate(60),
            EncoderKind.Nvenc)
        {
            EncoderPreference = EncoderPreference.Auto,
        };

        var next = await rollover.ReserveNextSoftwarePartAsync(
            plan,
            segmentNumber: 2,
            AudioRouting.MicOnly,
            CancellationToken.None);
        var stopped = new RecordingStopResult(
            first,
            VideoPacketCount: 90,
            AudioPacketCount: 142,
            new RecordingMediaExpectation(
                1_920,
                1_080,
                60,
                48_000,
                2,
                TimeSpan.FromSeconds(1.5)));
        await rollover.FinalizeIntermediatePartAsync(
            stopped,
            CancellationToken.None);

        Assert.Equal(directory, reservation.OutputPath!.FullPath);
        Assert.Equal(startedAt, reservation.Descriptor!.Timestamp);
        Assert.Equal(1_920, reservation.Descriptor.Width);
        Assert.Equal(1_080, reservation.Descriptor.Height);
        Assert.Equal(60, reservation.Descriptor.FrameRate.Value);
        Assert.Equal(2, reservation.Descriptor.SegmentNumber);
        Assert.Equal(second, next.Output);
        Assert.Equal(EncoderKind.MediaFoundationSoftware, next.Encoder);
        Assert.Equal(AudioRouting.MicOnly, next.Media.AudioRouting);
        Assert.Equal(plan.Signal, next.Signal);
        Assert.Equal(plan.VideoLayout, next.VideoLayout);
        Assert.Equal(stopped.MediaExpectation, validator.Expectation);
        Assert.Equal(first, finalizer.Recording);
        Assert.Equal(
            new FinalizedRecording(first.FinalPath),
            Assert.Single(saved.Recordings));
    }

    [Fact]
    public async Task ReservesExactPartAtTheNewStableSourceGeometry()
    {
        var directory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "vrrecorder-exact-rollover"));
        var first = new PendingRecording(
            Path.Combine(directory, "take.recording.mp4"),
            Path.Combine(directory, "take.mp4"));
        var second = new PendingRecording(
            Path.Combine(directory, "take_part002.recording.mp4"),
            Path.Combine(directory, "take_part002.mp4"));
        var reservation = new CapturingReservation(second);
        var rollover = new RecordingPartRollover(
            reservation,
            CreateFinalization());
        var initialSignal = new StableVideoSignal(
            "vrchat",
            42,
            "GPU_1234",
            GpuVendor.Nvidia,
            1_920,
            1_080,
            VideoPixelFormat.Bgra8,
            60);
        var initialLayout = RecordingVideoLayoutSession.StartExactSegment(
            initialSignal);
        var plan = new RecordingPlan(
            initialSignal,
            first,
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(60),
            EncoderKind.Nvenc,
            initialLayout)
        {
            Media = RecordingMediaConfiguration.CreateDefault()
                .WithVideoSource(initialSignal),
        };
        var nextSignal = initialSignal.WithGeometry(
            new VideoGeometry(1_280, 720, VideoPixelFormat.Rgba8));

        var next = await rollover.ReserveNextExactPartAsync(
            plan,
            nextSignal,
            segmentNumber: 2,
            AudioRouting.MicOnly,
            CancellationToken.None);

        Assert.Equal(1_280, reservation.Descriptor!.Width);
        Assert.Equal(720, reservation.Descriptor.Height);
        Assert.Equal(2, reservation.Descriptor.SegmentNumber);
        Assert.Equal(second, next.Output);
        Assert.Equal(EncoderKind.Nvenc, next.Encoder);
        Assert.Equal(nextSignal, next.Signal);
        Assert.Equal(AudioRouting.MicOnly, next.Media.AudioRouting);
        Assert.Equal("vrchat", next.Media.SpoutSenderIdentity);
        Assert.Equal(42UL, next.Media.SpoutAdapterLuid);
        Assert.Equal(ResolutionChangePolicy.ExactFollowSegments,
            next.VideoLayout.Policy);
        Assert.Equal(1_280, next.VideoLayout.CurrentLayout.Source.Width);
        Assert.Equal(720, next.VideoLayout.CurrentLayout.Source.Height);
        Assert.Equal(VideoPixelFormat.Rgba8,
            next.VideoLayout.CurrentLayout.Source.PixelFormat);
        Assert.Equal(1_280, next.VideoLayout.OutputCanvas.Width);
        Assert.Equal(720, next.VideoLayout.OutputCanvas.Height);
        Assert.Equal(VideoPixelFormat.Nv12,
            next.VideoLayout.OutputCanvas.PixelFormat);
        Assert.Equal(1_920, plan.VideoLayout.OutputCanvas.Width);
        Assert.Equal(1_080, plan.VideoLayout.OutputCanvas.Height);
        Assert.Throws<ArgumentException>(() =>
            RecordingVideoLayoutSession.StartExactSegment(
                nextSignal.WithGeometry(new VideoGeometry(
                    1_279,
                    720,
                    VideoPixelFormat.Rgba8))));
    }

    [Fact]
    public async Task SoftwareStartRetryRecreatesTheSameEmptyReservation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-rollover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pending = new PendingRecording(
            Path.Combine(directory, "take.recording.mp4"),
            Path.Combine(directory, "take.mp4"));

        try
        {
            await File.WriteAllTextAsync(
                pending.TemporaryPath,
                "partial hardware descriptor and packets");
            var plan = new RecordingPlan(
                new StableVideoSignal(320, 180),
                pending,
                new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
                new FrameRate(30),
                EncoderKind.Nvenc)
            {
                EncoderPreference = EncoderPreference.Auto,
            };
            var rollover = new RecordingPartRollover(
                new CapturingReservation(pending),
                new RecordingFileFinalizationUseCase(
                    new CapturingFinalizer(),
                    new CapturingValidator(),
                    new UnexpectedRecoveryStore(),
                    new CapturingSavedRecordingSink()));

            var retry = await rollover.PrepareSoftwareStartRetryAsync(
                plan,
                CancellationToken.None);

            Assert.Equal(pending, retry.Output);
            Assert.Equal(EncoderKind.MediaFoundationSoftware, retry.Encoder);
            Assert.True(File.Exists(pending.TemporaryPath));
            Assert.Equal(0, new FileInfo(pending.TemporaryPath).Length);
            Assert.False(File.Exists(pending.FinalPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConstructorRejectsMissingDependencies()
    {
        var finalization = CreateFinalization();

        Assert.Throws<ArgumentNullException>(() =>
            new RecordingPartRollover(null!, finalization));
        Assert.Throws<ArgumentNullException>(() =>
            new RecordingPartRollover(
                new CapturingReservation(CreatePending(Path.GetTempPath())),
                null!));
    }

    [Fact]
    public async Task SoftwareStartRetryRejectsUnsafeReservationReuse()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-rollover-reject-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pending = CreatePending(directory);
        var rollover = new RecordingPartRollover(
            new CapturingReservation(pending),
            CreateFinalization());

        try
        {
            var softwarePlan = CreatePlan(
                pending,
                EncoderKind.MediaFoundationSoftware);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rollover.PrepareSoftwareStartRetryAsync(
                    softwarePlan,
                    CancellationToken.None));

            await File.WriteAllTextAsync(pending.FinalPath, "occupied");
            var hardwarePlan = CreatePlan(pending, EncoderKind.Nvenc);
            await Assert.ThrowsAsync<IOException>(() =>
                rollover.PrepareSoftwareStartRetryAsync(
                    hardwarePlan,
                    CancellationToken.None));
            File.Delete(pending.FinalPath);

            Directory.CreateDirectory(pending.TemporaryPath);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                rollover.PrepareSoftwareStartRetryAsync(
                    hardwarePlan,
                    CancellationToken.None));
            Assert.True(Directory.Exists(pending.TemporaryPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RolloverOperationsRejectInvalidContracts()
    {
        var pending = CreatePending(Path.GetTempPath());
        var rollover = new RecordingPartRollover(
            new CapturingReservation(pending),
            CreateFinalization());
        var plan = CreatePlan(pending, EncoderKind.Nvenc);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rollover.ReserveNextSoftwarePartAsync(
                plan,
                segmentNumber: 1,
                AudioRouting.Mixed,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            rollover.ReserveNextSoftwarePartAsync(
                null!,
                segmentNumber: 2,
                AudioRouting.Mixed,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            rollover.FinalizeIntermediatePartAsync(
                null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task SoftwareFallbackRejectsFixedPreferenceBeforeFileMutation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-fixed-rollover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pending = CreatePending(directory);
        var reservation = new CapturingReservation(pending);
        var rollover = new RecordingPartRollover(
            reservation,
            CreateFinalization());
        var plan = CreatePlan(pending, EncoderKind.Nvenc) with
        {
            EncoderPreference = EncoderPreference.Nvenc,
        };

        try
        {
            await File.WriteAllTextAsync(
                pending.TemporaryPath,
                "fixed encoder partial output");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rollover.PrepareSoftwareStartRetryAsync(
                    plan,
                    CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rollover.ReserveNextSoftwarePartAsync(
                    plan,
                    segmentNumber: 2,
                    AudioRouting.Mixed,
                    CancellationToken.None));

            Assert.Equal(
                "fixed encoder partial output",
                await File.ReadAllTextAsync(pending.TemporaryPath));
            Assert.Null(reservation.OutputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RecordingFileFinalizationUseCase CreateFinalization() =>
        new(
            new CapturingFinalizer(),
            new CapturingValidator(),
            new UnexpectedRecoveryStore(),
            new CapturingSavedRecordingSink());

    private static PendingRecording CreatePending(string directory) => new(
        Path.Combine(directory, "take.recording.mp4"),
        Path.Combine(directory, "take.mp4"));

    private static RecordingPlan CreatePlan(
        PendingRecording pending,
        EncoderKind encoder) => new(
        new StableVideoSignal(320, 180),
        pending,
        new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
        new FrameRate(30),
        encoder)
    {
        EncoderPreference = EncoderPreference.Auto,
    };

    private sealed class CapturingReservation(PendingRecording result)
        : IRecordingFileReservation
    {
        public OutputPath? OutputPath { get; private set; }

        public RecordingFileDescriptor? Descriptor { get; private set; }

        public Task<PendingRecording> ReserveAsync(
            OutputPath outputPath,
            RecordingFileDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            Descriptor = descriptor;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingValidator : IRecordingFileValidator
    {
        public RecordingMediaExpectation? Expectation { get; private set; }

        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecordingFileValidation.Valid);

        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            RecordingMediaExpectation expectation,
            CancellationToken cancellationToken)
        {
            Expectation = expectation;
            return Task.FromResult(RecordingFileValidation.Valid);
        }
    }

    private sealed class CapturingFinalizer : IRecordingFileFinalizer
    {
        public PendingRecording? Recording { get; private set; }

        public Task<FinalizedRecording> FinalizeAsync(
            PendingRecording recording,
            CancellationToken cancellationToken)
        {
            Recording = recording;
            return Task.FromResult(new FinalizedRecording(recording.FinalPath));
        }
    }

    private sealed class CapturingSavedRecordingSink : ISavedRecordingSink
    {
        public List<FinalizedRecording> Recordings { get; } = [];

        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            Recordings.Add(recording);
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedRecoveryStore : IRecordingRecoveryStore
    {
        public Task<QuarantinedRecording> QuarantineAsync(
            RecoverableRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery was not expected.");
    }
}
