using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record RecordingPlan(
    StableVideoSignal Signal,
    PendingRecording Output,
    RecordingSessionTimestamp StartedAt,
    FrameRate FrameRate);
