using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingFaultStopFailure(
    RecordingHandle Handle,
    NativeRecordingFault Fault,
    Exception Exception);
