using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record AudioSessionStatus(
    AudioSessionStatusKind Kind,
    AudioInput Input,
    long FramePosition,
    TimeSpan? RediscoveryBudget = null);
