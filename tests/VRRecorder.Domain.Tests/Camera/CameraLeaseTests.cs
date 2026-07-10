using VRRecorder.Domain.Camera;

namespace VRRecorder.Domain.Tests.Camera;

public sealed class CameraLeaseTests
{
    [Fact]
    public void ChangedModeAndStreamingAreBothRestored()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

        var plan = lease.CreateRestorePlan();

        Assert.Equal(false, plan.Streaming);
        Assert.Equal(CameraMode.Photo, plan.Mode);
    }

    [Fact]
    public void ModeNotChangedByRecorderIsLeftUnchanged()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Known(CameraMode.Stream),
            ObservedCameraValue.Known(true),
            changedModeByRecorder: false,
            changedStreamingByRecorder: false);

        var plan = lease.CreateRestorePlan();

        Assert.Null(plan.Streaming);
        Assert.Null(plan.Mode);
    }

    [Fact]
    public void UnknownChangedModeIsNotGuessedDuringRestore()
    {
        var lease = new CameraLease(
            ObservedCameraValue.Unknown<CameraMode>(),
            ObservedCameraValue.Unknown<bool>(),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

        var plan = lease.CreateRestorePlan();

        Assert.Equal(false, plan.Streaming);
        Assert.Null(plan.Mode);
    }

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
