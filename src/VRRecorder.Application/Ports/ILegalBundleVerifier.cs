using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Ports;

public interface ILegalBundleVerifier
{
    Task<LegalBundleVerification> VerifyAsync(
        CancellationToken cancellationToken);
}
