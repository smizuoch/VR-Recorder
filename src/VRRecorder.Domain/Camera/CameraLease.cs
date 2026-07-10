namespace VRRecorder.Domain.Camera;

public sealed class CameraLease
{
    private readonly ObservedCameraValue<CameraMode> _previousMode;
    private readonly ObservedCameraValue<bool> _previousStreaming;
    private readonly bool _changedModeByRecorder;
    private readonly bool _changedStreamingByRecorder;

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
        ObservedCameraValue<CameraMode> previousMode,
        ObservedCameraValue<bool> previousStreaming,
        bool changedModeByRecorder,
        bool changedStreamingByRecorder)
    {
        _previousMode = previousMode;
        _previousStreaming = previousStreaming;
        _changedModeByRecorder = changedModeByRecorder;
        _changedStreamingByRecorder = changedStreamingByRecorder;
    }

    public CameraRestorePlan CreateRestorePlan(
        UnknownCameraStatePolicy unknownStatePolicy =
            UnknownCameraStatePolicy.DisableStreaming)
    {
        var streaming = StreamingRestoreValue(unknownStatePolicy);
        CameraMode? mode = _changedModeByRecorder && _previousMode.IsKnown
            ? _previousMode.Value
            : null;
        return new CameraRestorePlan(streaming, mode);
    }

    private bool? StreamingRestoreValue(
        UnknownCameraStatePolicy unknownStatePolicy)
    {
        if (!_changedStreamingByRecorder)
        {
            return null;
        }

        if (_previousStreaming.IsKnown)
        {
            return _previousStreaming.Value;
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
}
