using VRRecorder.Domain.Camera;

namespace VRRecorder.Domain.Tests.Camera;

public sealed class CameraLeaseTests
{
    [Fact]
    public void PersistentIdentityIsExposedWithoutChangingRestoreOwnership()
    {
        var identity = new CameraLeaseIdentity(
            "session-001",
            "VRChat-Client-1._oscjson._tcp.local.",
            processId: 4242,
            new DateTimeOffset(2026, 7, 10, 3, 4, 5, TimeSpan.Zero));
        var lease = new CameraLease(
            identity,
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

        Assert.Same(identity, lease.Identity);
        Assert.Equal(identity.SessionId, lease.SessionId);
        Assert.Equal(identity.VrChatServiceId, lease.VrChatServiceId);
        Assert.Equal(identity.ProcessId, lease.ProcessId);
        Assert.Equal(identity.CreatedAtUtc, lease.CreatedAtUtc);
        Assert.True(lease.IsPersistable);
        Assert.Equal(
            new CameraRestorePlan(false, CameraMode.Photo),
            lease.CreateRestorePlan());
    }

    [Fact]
    public void PersistentIdentityRejectsLocalTimeAndInvalidProcess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CameraLeaseIdentity(
                "session-001",
                "service-001",
                processId: 0,
                DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() =>
            new CameraLeaseIdentity(
                "session-001",
                "service-001",
                processId: 1,
                new DateTimeOffset(
                    2026,
                    7,
                    10,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(9))));
    }

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
