using System.Diagnostics;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Camera;

public sealed class SystemCameraLeaseOwnershipTests
{
    [Fact]
    public void IdentitySourceUsesInjectedClockAndCurrentProcess()
    {
        var localNow = new DateTimeOffset(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.FromHours(9));
        var source = new SystemCameraLeaseIdentitySource(
            new FixedWallClock(localNow));

        var first = source.Create("service-selected");
        var second = source.Create("service-selected");

        Assert.Equal("service-selected", first.VrChatServiceId);
        Assert.Equal(Environment.ProcessId, first.ProcessId);
        Assert.Equal(localNow.ToUniversalTime(), first.CreatedAtUtc);
        Assert.True(Guid.TryParseExact(first.SessionId, "N", out _));
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    [Fact]
    public async Task OwnerProbeRecognizesCurrentProcessStartedBeforeLease()
    {
        var probe = new SystemProcessCameraLeaseOwnerActivityProbe();
        var lease = Lease(Environment.ProcessId, DateTimeOffset.UtcNow);

        var active = await probe.IsOwnerActiveAsync(
            lease,
            CancellationToken.None);

        Assert.True(active);
    }

    [Fact]
    public async Task OwnerProbeRejectsPidReusedAfterLeaseWasCreated()
    {
        using var process = Process.GetCurrentProcess();
        var processStartedAtUtc = new DateTimeOffset(process.StartTime)
            .ToUniversalTime();
        var lease = Lease(
            process.Id,
            processStartedAtUtc.AddTicks(-1));
        var probe = new SystemProcessCameraLeaseOwnerActivityProbe();

        var active = await probe.IsOwnerActiveAsync(
            lease,
            CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task OwnerProbeReportsMissingProcessAsInactive()
    {
        var probe = new SystemProcessCameraLeaseOwnerActivityProbe();
        var lease = Lease(int.MaxValue, DateTimeOffset.UtcNow);

        var active = await probe.IsOwnerActiveAsync(
            lease,
            CancellationToken.None);

        Assert.False(active);
    }

    private static CameraLease Lease(
        int processId,
        DateTimeOffset createdAtUtc) =>
        new(
            "session-owner",
            "service-owner",
            processId,
            createdAtUtc,
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
    }
}
