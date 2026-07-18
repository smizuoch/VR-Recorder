using VRRecorder.Application.Audio;
using VRRecorder.Domain.Video;

namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingCallbacks(
    Action FirstVideoPacketMuxed,
    Action<NativeRecordingFault> Faulted,
    Action<AudioSessionWarning>? AudioWarning = null,
    Action<AudioSessionStatus>? AudioStatus = null,
    Action<NativeAvDriftEvent>? AvDrift = null,
    Action<RecordingAudioBufferHealthEvent>? AudioBufferHealth = null,
    Action<NativeRecordingFault>? VideoEncoderFailed = null,
    Action<VideoGeometry>? VideoGeometryStable = null);
