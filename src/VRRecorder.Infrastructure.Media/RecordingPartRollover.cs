using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
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

    public async Task FinalizeIntermediatePartAsync(
        RecordingStopResult stopped,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopped);
        _ = await _finalization
            .ExecuteAsync(stopped, cancellationToken)
            .ConfigureAwait(false);
    }
}
