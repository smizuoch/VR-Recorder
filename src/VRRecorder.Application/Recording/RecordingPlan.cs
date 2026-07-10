using VRRecorder.Application.Storage;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record RecordingPlan(
    StableVideoSignal Signal,
    PendingRecording Output,
    RecordingSessionTimestamp StartedAt,
    FrameRate FrameRate,
    EncoderKind Encoder,
    RecordingVideoLayoutSession VideoLayout)
{
    public RecordingPlan(
        StableVideoSignal signal,
        PendingRecording output,
        RecordingSessionTimestamp startedAt,
        FrameRate frameRate,
        EncoderKind encoder = EncoderKind.MediaFoundationSoftware)
        : this(
            signal,
            output,
            startedAt,
            frameRate,
            encoder,
            RecordingVideoLayoutSession.Start(
                signal,
                ResolutionChangePolicy.SingleFileFit))
    {
    }
}
