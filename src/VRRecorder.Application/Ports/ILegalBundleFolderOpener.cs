using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Ports;

public interface ILegalBundleFolderOpener
{
    Task<LegalFolderOpenResult> OpenAsync(
        string expectedBundleId,
        CancellationToken cancellationToken);
}
