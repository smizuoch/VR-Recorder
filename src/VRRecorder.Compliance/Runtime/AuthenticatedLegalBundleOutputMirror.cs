using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleOutputMirror
    : ILegalBundleOutputMirror
{
    private readonly string _installRoot;
    private readonly string _productVersion;
    private readonly AuthenticatedLegalBundleMirror _mirror;

    public AuthenticatedLegalBundleOutputMirror(
        string installRoot,
        string productVersion,
        AuthenticatedLegalBundleVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        ArgumentNullException.ThrowIfNull(verifier);
        _installRoot = Path.GetFullPath(installRoot);
        _productVersion = productVersion;
        _mirror = new AuthenticatedLegalBundleMirror(
            verifier,
            LegalBundleVerificationScope.InstallRoot);
    }

    public async Task MirrorAsync(
        OutputPath outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        cancellationToken.ThrowIfCancellationRequested();
        _ = await _mirror
            .MirrorAsync(
                _installRoot,
                outputPath.FullPath,
                _productVersion,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
