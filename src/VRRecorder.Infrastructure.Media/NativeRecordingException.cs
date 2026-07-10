namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingException : Exception
{
    public NativeRecordingException(NativeRecordingFault fault)
        : this(fault, innerException: null)
    {
    }

    public NativeRecordingException(
        NativeRecordingFault fault,
        Exception? innerException)
        : base(fault?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(fault);
        Fault = fault;
    }

    public NativeRecordingFault Fault { get; }
}
