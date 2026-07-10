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

    public CameraRestorePlan CreateRestorePlan()
    {
        if (!_changedStreamingByRecorder)
        {
            return new CameraRestorePlan(null);
        }

        var restoredStreaming = _previousStreaming.IsKnown
            ? _previousStreaming.Value
            : false;
        return new CameraRestorePlan(restoredStreaming);
    }
}
