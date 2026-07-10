using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public interface INativeRecordingRuntimeFaultSink
{
    void Report(
        RecordingHandle handle,
        NativeRecordingFault fault);
}
