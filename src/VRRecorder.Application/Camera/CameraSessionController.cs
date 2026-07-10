using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Camera;

public sealed class CameraSessionController
{
    private readonly IVrChatCameraGateway _gateway;
    private readonly ICameraLeaseStore _leaseStore;
    private readonly ICameraLeaseIdentitySource? _leaseIdentities;

    public CameraSessionController(
        IVrChatCameraGateway gateway,
        ICameraLeaseStore leaseStore)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(leaseStore);
        _gateway = gateway;
        _leaseStore = leaseStore;
    }

    public CameraSessionController(
        IVrChatCameraGateway gateway,
        ICameraLeaseStore leaseStore,
        ICameraLeaseIdentitySource leaseIdentities)
        : this(gateway, leaseStore)
    {
        ArgumentNullException.ThrowIfNull(leaseIdentities);
        _leaseIdentities = leaseIdentities;
    }

    public async Task<CameraLease> AcquireAsync(
        CameraSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return await AcquireCoreAsync(
                snapshot,
                identity: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CameraLease> AcquireAsync(
        CameraSnapshot snapshot,
        string vrChatServiceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        var source = _leaseIdentities ??
                     throw new InvalidOperationException(
                         "Persistent camera acquisition requires an identity source.");
        cancellationToken.ThrowIfCancellationRequested();
        var identity = source.Create(vrChatServiceId);
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(
                identity.VrChatServiceId,
                vrChatServiceId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The camera lease identity does not match the selected VRChat service.");
        }

        return await AcquireCoreAsync(snapshot, identity, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CameraLease> AcquireCoreAsync(
        CameraSnapshot snapshot,
        CameraLeaseIdentity? identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changeMode = !snapshot.Mode.IsKnown ||
                         snapshot.Mode.Value != CameraMode.Stream;
        var changeStreaming = !snapshot.Streaming.IsKnown ||
                              !snapshot.Streaming.Value;
        var lease = identity is null
            ? new CameraLease(
                snapshot.Mode,
                snapshot.Streaming,
                changeMode,
                changeStreaming)
            : new CameraLease(
                identity,
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
