namespace VRRecorder.Domain.Camera;

public sealed class CameraLease
{
    private readonly ObservedCameraValue<bool> _previousStreaming;
    private readonly bool _changedStreamingByRecorder;

    public CameraLease(
        ObservedCameraValue<bool> previousStreaming,
        bool changedStreamingByRecorder)
    {
        _previousStreaming = previousStreaming;
        _changedStreamingByRecorder = changedStreamingByRecorder;
    }

    public CameraRestorePlan CreateRestorePlan(
        UnknownCameraStatePolicy unknownStatePolicy =
            UnknownCameraStatePolicy.DisableStreaming)
    {
        if (!_changedStreamingByRecorder)
        {
            return new CameraRestorePlan(null);
        }

        if (!_previousStreaming.IsKnown)
        {
            return unknownStatePolicy switch
            {
                UnknownCameraStatePolicy.DisableStreaming =>
                    new CameraRestorePlan(false),
                UnknownCameraStatePolicy.LeaveUnchanged =>
                    new CameraRestorePlan(null),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(unknownStatePolicy),
                    unknownStatePolicy,
                    "Unknown camera-state policy."),
            };
        }

        return new CameraRestorePlan(_previousStreaming.Value);
    }
}
