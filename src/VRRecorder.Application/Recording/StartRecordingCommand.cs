using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Recording;

public sealed record StartRecordingCommand(
    SelfTimer SelfTimer,
    RecordingDuration AutoStop);
