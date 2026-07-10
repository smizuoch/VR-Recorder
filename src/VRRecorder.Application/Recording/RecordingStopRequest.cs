namespace VRRecorder.Application.Recording;

public sealed record RecordingStopRequest(
    RecordingHandle Handle,
    RecordingStopReason Reason);
