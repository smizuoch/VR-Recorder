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

        if (!_previousStreaming.IsKnown)
        {
            return new CameraRestorePlan(false);
        }

        return new CameraRestorePlan(_previousStreaming.Value);
    }
}
