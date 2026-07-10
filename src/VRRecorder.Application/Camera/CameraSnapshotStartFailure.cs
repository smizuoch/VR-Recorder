namespace VRRecorder.Application.Camera;

public sealed record CameraSnapshotStartFailure
{
    public CameraSnapshotStartFailure(
        CameraSnapshotStartFailureKind kind,
        string vrChatServiceId,
        Exception failure)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown camera snapshot start failure kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        ArgumentNullException.ThrowIfNull(failure);
        Kind = kind;
        VrChatServiceId = vrChatServiceId;
        Failure = failure;
    }

    public CameraSnapshotStartFailureKind Kind { get; }

    public string VrChatServiceId { get; }

    public Exception Failure { get; }
}
