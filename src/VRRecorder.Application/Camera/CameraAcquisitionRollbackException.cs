namespace VRRecorder.Application.Camera;

internal sealed class CameraAcquisitionRollbackException : Exception
{
    public CameraAcquisitionRollbackException(
        Exception acquisitionFailure,
        Exception restorationFailure)
        : base(
            "Camera acquisition failed and its ownership rollback also failed.",
            acquisitionFailure)
    {
        ArgumentNullException.ThrowIfNull(acquisitionFailure);
        ArgumentNullException.ThrowIfNull(restorationFailure);
        AcquisitionFailure = acquisitionFailure;
        RestorationFailure = restorationFailure;
    }

    public Exception AcquisitionFailure { get; }

    public Exception RestorationFailure { get; }
}
