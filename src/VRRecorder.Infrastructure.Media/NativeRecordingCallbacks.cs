namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingCallbacks(
    Action FirstVideoPacketMuxed,
    Action<NativeRecordingFault> Faulted);
