namespace VRRecorder.Infrastructure.Media;

public sealed record NativeAvDriftEvent(
    TimeSpan VideoPts,
    TimeSpan AudioPts,
    TimeSpan AbsoluteDrift);
