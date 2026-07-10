using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Camera;

public sealed class CameraSessionController
{
    private readonly IVrChatCameraGateway _gateway;
    private readonly ICameraLeaseStore _leaseStore;

    public CameraSessionController(
        IVrChatCameraGateway gateway,
        ICameraLeaseStore leaseStore)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(leaseStore);
        _gateway = gateway;
        _leaseStore = leaseStore;
    }

    public async Task<CameraLease> AcquireAsync(
        CameraSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changeMode = !snapshot.Mode.IsKnown ||
                         snapshot.Mode.Value != CameraMode.Stream;
        var changeStreaming = !snapshot.Streaming.IsKnown ||
                              !snapshot.Streaming.Value;
        var lease = new CameraLease(
            snapshot.Mode,
            snapshot.Streaming,
            changeMode,
            changeStreaming);

        await _leaseStore
            .SaveAsync(lease, cancellationToken)
            .ConfigureAwait(false);
        if (changeMode)
        {
            await _gateway
                .SetModeAsync(CameraMode.Stream, cancellationToken)
                .ConfigureAwait(false);
        }

        if (changeStreaming)
        {
            await _gateway
                .SetStreamingAsync(true, cancellationToken)
                .ConfigureAwait(false);
        }

        return lease;
    }

    public async Task RestoreAsync(
        CameraLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var plan = lease.CreateRestorePlan();
        if (plan.Streaming is { } streaming)
        {
            await _gateway
                .SetStreamingAsync(streaming, cancellationToken)
                .ConfigureAwait(false);
        }

        if (plan.Mode is { } mode)
        {
            await _gateway
                .SetModeAsync(mode, cancellationToken)
                .ConfigureAwait(false);
        }

        await _leaseStore
            .DeleteAsync(lease, cancellationToken)
            .ConfigureAwait(false);
    }
}
