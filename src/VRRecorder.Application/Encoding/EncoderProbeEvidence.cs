using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Encoding;

public sealed record EncoderProbeEvidence(
    EncoderKind ActualEncoder,
    string CodecName,
    bool HardwareAccelerated,
    ulong AdapterLuid,
    EncoderInputFormat InputFormat,
    int Width,
    int Height,
    FrameRate FrameRate,
    EncoderProbeValidation Validation,
    string DriverIdentity,
    string FfmpegBuildIdentity,
    string Profile,
    string DeviceIdentity);
