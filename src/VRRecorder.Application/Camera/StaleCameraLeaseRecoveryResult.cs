namespace VRRecorder.Application.Camera;

public abstract record StaleCameraLeaseRecoveryResult
{
    private StaleCameraLeaseRecoveryResult()
    {
    }

    public sealed record NoLease : StaleCameraLeaseRecoveryResult;

    public sealed record OwnerStillActive(string SessionId)
        : StaleCameraLeaseRecoveryResult;

    public sealed record Restored(string SessionId)
        : StaleCameraLeaseRecoveryResult;

    public sealed record Failed(string Code, string? SessionId)
        : StaleCameraLeaseRecoveryResult;
}
