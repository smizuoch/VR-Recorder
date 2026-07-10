namespace VRRecorder.Domain.Storage;

public readonly record struct RecordingSessionTimestamp
{
    public RecordingSessionTimestamp(DateTimeOffset localStartedAt)
    {
        LocalStartedAt = localStartedAt;
    }

    public DateTimeOffset LocalStartedAt { get; }

    public DateTimeOffset UtcStartedAt => LocalStartedAt.ToUniversalTime();
}
