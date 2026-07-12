namespace VRRecorder.Application.Recording;

public sealed record RecordingAvDriftEvent(
    TimeSpan VideoPts,
    TimeSpan AudioPts,
    TimeSpan AbsoluteDrift);
