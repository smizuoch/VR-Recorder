using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Desktop;

public sealed class LegalBundleMirroringDesktopRecordingStartRequestSource
    : IDesktopRecordingStartRequestSource
{
    private readonly IDesktopRecordingStartRequestSource _inner;
    private readonly ILegalBundleOutputMirror _legalBundleMirror;

    public LegalBundleMirroringDesktopRecordingStartRequestSource(
        IDesktopRecordingStartRequestSource inner,
        ILegalBundleOutputMirror legalBundleMirror)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(legalBundleMirror);
        _inner = inner;
        _legalBundleMirror = legalBundleMirror;
    }

    public async Task<DesktopRecordingStartRequest> GetAsync(
        CancellationToken cancellationToken)
    {
        var request = await _inner
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await _legalBundleMirror
            .MirrorAsync(request.Command.OutputPath, cancellationToken)
            .ConfigureAwait(false);
        return request;
    }
}
