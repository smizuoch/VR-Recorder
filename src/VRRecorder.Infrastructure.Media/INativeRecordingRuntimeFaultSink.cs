namespace VRRecorder.Infrastructure.Media;

public interface INativeRecordingRuntimeFaultSink
{
    void Report(NativeRecordingFault fault);
}
