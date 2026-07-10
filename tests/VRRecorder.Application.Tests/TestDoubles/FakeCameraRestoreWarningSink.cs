using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeCameraRestoreWarningSink
    : ICameraRestoreWarningSink
{
    public List<CameraRestoreWarning> Warnings { get; } = [];

    public Task PublishAsync(
        CameraRestoreWarning warning,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Warnings.Add(warning);
        return Task.CompletedTask;
    }
}
