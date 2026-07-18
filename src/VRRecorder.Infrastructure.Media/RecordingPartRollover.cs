using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Infrastructure.Media;

public sealed class RecordingPartRollover(
    IRecordingFileReservation reservation,
    RecordingFileFinalizationUseCase finalization)
    : IRecordingPartRollover
{
    private readonly IRecordingFileReservation _reservation =
        reservation ?? throw new ArgumentNullException(nameof(reservation));
    private readonly RecordingFileFinalizationUseCase _finalization =
        finalization ?? throw new ArgumentNullException(nameof(finalization));

    public Task<RecordingPlan> PrepareSoftwareStartRetryAsync(
        RecordingPlan failedPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failedPlan);
        cancellationToken.ThrowIfCancellationRequested();
        if (failedPlan.Encoder == EncoderKind.MediaFoundationSoftware)
        {
            throw new InvalidOperationException(
                "A software recording start cannot fall back to itself.");
        }

        var recording = failedPlan.Output;
        if (File.Exists(recording.FinalPath))
        {
            throw new IOException(
                "The reserved final recording path already exists.");
        }

        try
        {
            File.Delete(recording.TemporaryPath);
            using var stream = new FileStream(
                recording.TemporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            try
            {
                File.Delete(recording.TemporaryPath);
            }
            catch (Exception)
            {
                // Preserve the reset failure that owns this start attempt.
            }

            throw;
        }

        return Task.FromResult(failedPlan with
        {
            Encoder = EncoderKind.MediaFoundationSoftware,
        });
    }

    public async Task<RecordingPlan> ReserveNextSoftwarePartAsync(
        RecordingPlan currentPlan,
        int segmentNumber,
        AudioRouting audioRouting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentPlan);
        if (segmentNumber < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentNumber),
                segmentNumber,
                "A rollover segment number must be at least two.");
        }

        var outputCanvas = currentPlan.VideoLayout.OutputCanvas;
        var descriptor = new RecordingFileDescriptor(
            currentPlan.StartedAt,
            outputCanvas.Width,
            outputCanvas.Height,
            currentPlan.FrameRate,
            segmentNumber);
        var outputPath = new OutputPath(
            Path.GetDirectoryName(currentPlan.Output.FinalPath) ??
            throw new InvalidOperationException(
                "The current recording has no output directory."));
        var output = await _reservation
            .ReserveAsync(outputPath, descriptor, cancellationToken)
            .ConfigureAwait(false);

        return currentPlan with
        {
            Output = output,
            Encoder = EncoderKind.MediaFoundationSoftware,
            Media = currentPlan.Media.WithAudioRouting(audioRouting),
        };
    }

    public async Task<RecordingPlan> ReserveNextExactPartAsync(
        RecordingPlan currentPlan,
        StableVideoSignal nextSignal,
        int segmentNumber,
        AudioRouting audioRouting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentPlan);
        ArgumentNullException.ThrowIfNull(nextSignal);
        if (currentPlan.VideoLayout.Policy !=
            ResolutionChangePolicy.ExactFollowSegments)
        {
            throw new InvalidOperationException(
                "Only an exact-follow recording can reserve an exact part.");
        }
        EnsureSameSourceIdentity(currentPlan.Signal, nextSignal);
        if (segmentNumber < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentNumber),
                segmentNumber,
                "A rollover segment number must be at least two.");
        }

        var nextLayout = RecordingVideoLayoutSession.StartExactSegment(
            nextSignal);
        var outputCanvas = nextLayout.OutputCanvas;
        var descriptor = new RecordingFileDescriptor(
            currentPlan.StartedAt,
            outputCanvas.Width,
            outputCanvas.Height,
            currentPlan.FrameRate,
            segmentNumber);
        var outputPath = new OutputPath(
            Path.GetDirectoryName(currentPlan.Output.FinalPath) ??
            throw new InvalidOperationException(
                "The current recording has no output directory."));
        var output = await _reservation
            .ReserveAsync(outputPath, descriptor, cancellationToken)
            .ConfigureAwait(false);

        return currentPlan with
        {
            Signal = nextSignal,
            Output = output,
            VideoLayout = nextLayout,
            Media = currentPlan.Media
                .WithAudioRouting(audioRouting)
                .WithVideoSource(nextSignal),
        };
    }

    public async Task FinalizeIntermediatePartAsync(
        RecordingStopResult stopped,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopped);
        _ = await _finalization
            .ExecuteAsync(stopped, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureSameSourceIdentity(
        StableVideoSignal current,
        StableVideoSignal next)
    {
        if (current.AdapterLuid != next.AdapterLuid ||
            current.GpuVendor != next.GpuVendor ||
            current.HasDiscoveredSourceIdentity !=
                next.HasDiscoveredSourceIdentity ||
            !string.Equals(
                current.SenderId,
                next.SenderId,
                StringComparison.Ordinal) ||
            !string.Equals(
                current.GpuIdentity,
                next.GpuIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An exact part must retain the active Spout source identity.",
                nameof(next));
        }
    }
}
