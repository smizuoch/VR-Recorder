namespace VRRecorder.Domain.Camera;

public sealed record CameraLeaseIdentity
{
    public CameraLeaseIdentity(
        string sessionId,
        string vrChatServiceId,
        int processId,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        if (sessionId.Any(char.IsControl) || vrChatServiceId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Camera lease identities cannot contain control characters.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        if (createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Camera lease creation time must be a non-default UTC value.",
                nameof(createdAtUtc));
        }

        SessionId = sessionId;
        VrChatServiceId = vrChatServiceId;
        ProcessId = processId;
        CreatedAtUtc = createdAtUtc;
    }

    public string SessionId { get; }

    public string VrChatServiceId { get; }

    public int ProcessId { get; }

    public DateTimeOffset CreatedAtUtc { get; }
}
