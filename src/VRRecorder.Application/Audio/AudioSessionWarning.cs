using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record AudioSessionWarning(
    AudioSessionWarningKind Kind,
    AudioInput Input,
    long FramePosition,
    Exception? Failure = null);
