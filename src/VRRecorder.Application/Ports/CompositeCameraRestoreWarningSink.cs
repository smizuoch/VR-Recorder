using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Ports;

public sealed class CompositeCameraRestoreWarningSink
    : ICameraRestoreWarningSink
{
    private readonly ICameraRestoreWarningSink _first;
    private readonly ICameraRestoreWarningSink _second;

    public CompositeCameraRestoreWarningSink(
        ICameraRestoreWarningSink first,
        ICameraRestoreWarningSink second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    public async Task PublishAsync(
        CameraRestoreWarning warning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warning);
        cancellationToken.ThrowIfCancellationRequested();
        await _first
            .PublishAsync(warning, cancellationToken)
            .ConfigureAwait(false);
        await _second
            .PublishAsync(warning, cancellationToken)
            .ConfigureAwait(false);
    }
}
