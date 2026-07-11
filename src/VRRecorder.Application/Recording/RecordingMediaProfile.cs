using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record RecordingMediaProfile(
    int SourceWidth,
    int SourceHeight,
    VideoPixelFormat SourcePixelFormat,
    double EstimatedSourceFramesPerSecond,
    int OutputWidth,
    int OutputHeight,
    int OutputFramesPerSecond,
    EncoderKind Encoder,
    GpuVendor GpuVendor);
