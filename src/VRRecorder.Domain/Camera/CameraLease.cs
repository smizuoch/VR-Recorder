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
            new CameraLeaseIdentity(
                sessionId,
                vrChatServiceId,
                processId,
                createdAtUtc),
            previousMode,
            previousStreaming,
            changedModeByRecorder,
            changedStreamingByRecorder)
    {
    }

    public CameraLease(
        CameraLeaseIdentity identity,
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
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
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

    public CameraLeaseIdentity? Identity { get; }

    public string? SessionId => Identity?.SessionId;

    public string? VrChatServiceId => Identity?.VrChatServiceId;

    public int ProcessId => Identity?.ProcessId ?? 0;

    public DateTimeOffset CreatedAtUtc => Identity?.CreatedAtUtc ?? default;

    public ObservedCameraValue<CameraMode> PreviousMode { get; }

    public ObservedCameraValue<bool> PreviousStreaming { get; }

    public bool ChangedModeByRecorder { get; }

    public bool ChangedStreamingByRecorder { get; }

    public bool IsPersistable => Identity is not null;

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
        return Identity == other.Identity &&
               PreviousMode == other.PreviousMode &&
               PreviousStreaming == other.PreviousStreaming &&
               ChangedModeByRecorder == other.ChangedModeByRecorder &&
               ChangedStreamingByRecorder == other.ChangedStreamingByRecorder;
    }

}
