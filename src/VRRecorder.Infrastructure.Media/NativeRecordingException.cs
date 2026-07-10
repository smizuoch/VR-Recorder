namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingException : Exception
{
    public NativeRecordingException(NativeRecordingFault fault)
        : base(fault?.Message)
    {
        ArgumentNullException.ThrowIfNull(fault);
        Fault = fault;
    }

    public NativeRecordingFault Fault { get; }
}
