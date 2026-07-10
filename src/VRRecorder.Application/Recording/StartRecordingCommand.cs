using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record StartRecordingCommand(
    SelfTimer SelfTimer,
    RecordingDuration AutoStop,
    OutputPath OutputPath,
    FrameRate FrameRate);
