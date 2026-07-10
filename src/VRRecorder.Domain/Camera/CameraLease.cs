namespace VRRecorder.Domain.Camera;

public sealed class CameraLease
{
    public CameraLease(
        ObservedCameraValue<bool> previousStreaming,
        bool changedStreamingByRecorder)
        : this(
            ObservedCameraValue.Unknown<CameraMode>(),
            previousStreaming,
            changedModeByRecorder: false,
            changedStreamingByRecorder)
    {
    }

    public CameraLease(
        string sessionId,
        string vrChatServiceId,
        int processId,
        DateTimeOffset createdAtUtc,
        ObservedCameraValue<CameraMode> previousMode,
        ObservedCameraValue<bool> previousStreaming,
        bool changedModeByRecorder,
        bool changedStreamingByRecorder)
        : this(
            previousMode,
            previousStreaming,
            changedModeByRecorder,
            changedStreamingByRecorder)
    {
        ValidateIdentity(
            sessionId,
            vrChatServiceId,
            processId,
            createdAtUtc);
        SessionId = sessionId;
        VrChatServiceId = vrChatServiceId;
        ProcessId = processId;
        CreatedAtUtc = createdAtUtc;
    }

    public CameraLease(
        ObservedCameraValue<CameraMode> previousMode,
        ObservedCameraValue<bool> previousStreaming,
        bool changedModeByRecorder,
        bool changedStreamingByRecorder)
    {
        PreviousMode = previousMode;
        PreviousStreaming = previousStreaming;
        ChangedModeByRecorder = changedModeByRecorder;
        ChangedStreamingByRecorder = changedStreamingByRecorder;
    }

    public string? SessionId { get; }

    public string? VrChatServiceId { get; }

    public int ProcessId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public ObservedCameraValue<CameraMode> PreviousMode { get; }

    public ObservedCameraValue<bool> PreviousStreaming { get; }

    public bool ChangedModeByRecorder { get; }

    public bool ChangedStreamingByRecorder { get; }

    public bool IsPersistable =>
        SessionId is not null &&
        VrChatServiceId is not null &&
        ProcessId > 0 &&
        CreatedAtUtc != default;

    public CameraRestorePlan CreateRestorePlan(
        UnknownCameraStatePolicy unknownStatePolicy =
            UnknownCameraStatePolicy.DisableStreaming)
    {
        var streaming = StreamingRestoreValue(unknownStatePolicy);
        CameraMode? mode = ChangedModeByRecorder && PreviousMode.IsKnown
            ? PreviousMode.Value
            : null;
        return new CameraRestorePlan(streaming, mode);
    }

    private bool? StreamingRestoreValue(
        UnknownCameraStatePolicy unknownStatePolicy)
    {
        if (!ChangedStreamingByRecorder)
        {
            return null;
        }

        if (PreviousStreaming.IsKnown)
        {
            return PreviousStreaming.Value;
        }

        return unknownStatePolicy switch
        {
            UnknownCameraStatePolicy.DisableStreaming => false,
            UnknownCameraStatePolicy.LeaveUnchanged => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(unknownStatePolicy),
                unknownStatePolicy,
                "Unknown camera-state policy."),
        };
    }

    public bool HasSamePersistedState(CameraLease other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
               string.Equals(
                   VrChatServiceId,
                   other.VrChatServiceId,
                   StringComparison.Ordinal) &&
               ProcessId == other.ProcessId &&
               CreatedAtUtc == other.CreatedAtUtc &&
               PreviousMode == other.PreviousMode &&
               PreviousStreaming == other.PreviousStreaming &&
               ChangedModeByRecorder == other.ChangedModeByRecorder &&
               ChangedStreamingByRecorder == other.ChangedStreamingByRecorder;
    }

    private static void ValidateIdentity(
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
    }
}
