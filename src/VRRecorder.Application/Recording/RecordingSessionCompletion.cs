using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Recording;

public sealed record RecordingSessionCompletion(
    RecordingHandle Handle,
    RecordingStopReason Reason,
    RecorderState FinalState);
