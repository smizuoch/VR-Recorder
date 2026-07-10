using VRRecorder.Domain.Camera;

namespace VRRecorder.Domain.Tests.Camera;

public sealed class CameraLeaseTests
{
    [Fact]
    public void StreamingChangedFromFalseIsRestoredToFalse()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Known(false),
            changedStreamingByRecorder: true);

        var plan = lease.CreateRestorePlan();

        Assert.Equal(false, plan.Streaming);
    }

    [Fact]
    public void StreamingAlreadyTrueIsLeftUnchanged()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Known(true),
            changedStreamingByRecorder: false);

        var plan = lease.CreateRestorePlan();

        Assert.Null(plan.Streaming);
    }

    [Fact]
    public void UnknownStreamingChangedByRecorderIsDisabledByDefault()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Unknown<bool>(),
            changedStreamingByRecorder: true);

        var plan = lease.CreateRestorePlan();

        Assert.Equal(false, plan.Streaming);
    }

    [Fact]
    public void UnknownStreamingCanBeLeftUnchangedByPolicy()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Unknown<bool>(),
            changedStreamingByRecorder: true);

        var plan = lease.CreateRestorePlan(
            UnknownCameraStatePolicy.LeaveUnchanged);

        Assert.Null(plan.Streaming);
    }
}
