using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Camera;

public sealed class StaleCameraLeaseRecoveryUseCase
{
    private readonly ICameraLeaseStore _leases;
    private readonly ICameraLeaseOwnerActivityProbe _ownerActivity;
    private readonly VrChatCameraConnectionUseCase _connections;
    private readonly ICameraRestoreWarningSink _warnings;

    public StaleCameraLeaseRecoveryUseCase(
        ICameraLeaseStore leases,
        ICameraLeaseOwnerActivityProbe ownerActivity,
        VrChatCameraConnectionUseCase connections,
        ICameraRestoreWarningSink warnings)
    {
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(ownerActivity);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(warnings);
        _leases = leases;
        _ownerActivity = ownerActivity;
        _connections = connections;
        _warnings = warnings;
    }

    public async Task<StaleCameraLeaseRecoveryResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        CameraLease? lease;
        try
        {
            lease = await _leases
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(
                    "CAMERA_LEASE_INVALID",
                    sessionId: null,
                    "The persisted camera lease could not be read safely.",
                    exception)
                .ConfigureAwait(false);
        }

        if (lease is null)
        {
            return new StaleCameraLeaseRecoveryResult.NoLease();
        }

        var sessionId = lease.SessionId ??
                        throw new InvalidDataException(
                            "A persisted camera lease has no session identity.");
        bool ownerActive;
        try
        {
            ownerActive = await _ownerActivity
                .IsOwnerActiveAsync(lease, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(
                    "CAMERA_LEASE_OWNER_CHECK_FAILED",
                    sessionId,
                    "The camera lease owner could not be checked safely.",
                    exception)
                .ConfigureAwait(false);
        }

        if (ownerActive)
        {
            return new StaleCameraLeaseRecoveryResult.OwnerStillActive(sessionId);
        }

        if (!lease.ChangedStreamingByRecorder)
        {
            try
            {
                await _leases
                    .DeleteAsync(lease, cancellationToken)
                    .ConfigureAwait(false);
                return new StaleCameraLeaseRecoveryResult.Restored(sessionId);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return await FailAsync(
                        "CAMERA_LEASE_RESTORE_FAILED",
                        sessionId,
                        "The stale VRChat camera state could not be restored.",
                        exception)
                    .ConfigureAwait(false);
            }
        }

        var serviceId = lease.VrChatServiceId ??
                        throw new InvalidDataException(
                            "A persisted camera lease has no VRChat service identity.");
        VrChatCameraConnectionResolution connection;
        try
        {
            connection = await _connections
                .ResolveAsync(serviceId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(
                    "CAMERA_LEASE_TARGET_DISCOVERY_FAILED",
                    sessionId,
                    "The VRChat target for the stale camera lease could not be discovered.",
                    exception)
                .ConfigureAwait(false);
        }

        if (connection is not VrChatCameraConnectionResolution.Connected connected ||
            !string.Equals(
                connected.Candidate.ServiceId,
                serviceId,
                StringComparison.Ordinal))
        {
            return await FailAsync(
                    "CAMERA_LEASE_TARGET_NOT_FOUND",
                    sessionId,
                    "The exact VRChat target for the stale camera lease is unavailable.")
                .ConfigureAwait(false);
        }

        try
        {
            await connected.Gateway
                .SetStreamingAsync(enabled: false, cancellationToken)
                .ConfigureAwait(false);
            await _leases
                .DeleteAsync(lease, cancellationToken)
                .ConfigureAwait(false);
            return new StaleCameraLeaseRecoveryResult.Restored(sessionId);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(
                    "CAMERA_LEASE_RESTORE_FAILED",
                    sessionId,
                    "The stale VRChat camera state could not be restored.",
                    exception)
                .ConfigureAwait(false);
        }
        finally
        {
            await DisposeGatewayBestEffortAsync(connected.Gateway)
                .ConfigureAwait(false);
        }
    }

    private async Task<StaleCameraLeaseRecoveryResult.Failed> FailAsync(
        string code,
        string? sessionId,
        string message,
        Exception? innerException = null)
    {
        var failure = new StaleCameraLeaseRecoveryException(
            code,
            message,
            innerException);
        try
        {
            await _warnings
                .PublishAsync(
                    new CameraRestoreWarning(
                        CameraRestoreWarningReason.StaleLeaseRecovery,
                        failure),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Warning delivery cannot destroy the persisted repair evidence.
        }

        return new StaleCameraLeaseRecoveryResult.Failed(code, sessionId);
    }

    private static async ValueTask DisposeGatewayBestEffortAsync(
        IVrChatCameraGateway gateway)
    {
        if (gateway is not IAsyncDisposable disposable)
        {
            return;
        }

        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A completed restore must not be reclassified by disposal diagnostics.
        }
    }
}
