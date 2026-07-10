using System.Diagnostics;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Storage;

public sealed class SystemProcessCameraLeaseOwnerActivityProbe
    : ICameraLeaseOwnerActivityProbe
{
    public ValueTask<bool> IsOwnerActiveAsync(
        CameraLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsPersistable)
        {
            throw new ArgumentException(
                "Owner activity requires a persistent camera lease.",
                nameof(lease));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(lease.ProcessId);
            DateTimeOffset startedAtUtc;
            try
            {
                if (process.HasExited)
                {
                    return ValueTask.FromResult(false);
                }

                startedAtUtc = new DateTimeOffset(process.StartTime)
                    .ToUniversalTime();
                if (process.HasExited)
                {
                    return ValueTask.FromResult(false);
                }
            }
            catch (InvalidOperationException)
            {
                return ValueTask.FromResult(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(startedAtUtc <= lease.CreatedAtUtc);
        }
        catch (ArgumentException)
        {
            return ValueTask.FromResult(false);
        }
    }
}
