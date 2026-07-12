using VRRecorder.Application.Audio;

namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingCallbacks(
    Action FirstVideoPacketMuxed,
    Action<NativeRecordingFault> Faulted,
    Action<AudioSessionWarning>? AudioWarning = null,
    Action<AudioSessionStatus>? AudioStatus = null,
    Action<NativeAvDriftEvent>? AvDrift = null);
