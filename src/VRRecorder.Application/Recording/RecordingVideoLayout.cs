using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record RecordingVideoLayout(
    VideoGeometry Source,
    VideoGeometry OutputCanvas,
    VideoPlacement Placement,
    VideoCanvasBackground Background,
    VideoRotation Rotation);
