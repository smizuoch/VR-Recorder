using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Recording;

public sealed record StartRecordingCommand(
    SelfTimer SelfTimer,
    RecordingDuration AutoStop,
    OutputPath OutputPath,
    FrameRate FrameRate,
    EncoderPreference EncoderPreference = EncoderPreference.Auto,
    GpuVendor GpuVendor = GpuVendor.Unknown,
    ResolutionChangePolicy ResolutionChangePolicy =
        ResolutionChangePolicy.SingleFileFit);
