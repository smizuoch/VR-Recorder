namespace VRRecorder.Infrastructure.Osc;

public sealed class OscQueryTimeoutException : TimeoutException
{
    public OscQueryTimeoutException(
        TimeSpan timeout,
        Exception innerException)
        : base(
            $"OSCQuery discovery did not complete within {timeout.TotalMilliseconds:0} ms.",
            innerException)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
