using ApplicationIssue = VRRecorder.Application.Compliance.LegalBundleIssue;
using ApplicationVerification =
    VRRecorder.Application.Compliance.LegalBundleVerification;
using VRRecorder.Application.Ports;

namespace VRRecorder.Compliance.Runtime;

public sealed class RuntimeLegalBundleVerificationGateway
    : ILegalBundleVerifier
{
    private readonly string _bundleDirectory;
    private readonly AuthenticatedLegalBundleVerifier _verifier;

    public RuntimeLegalBundleVerificationGateway(
        string bundleDirectory,
        AuthenticatedLegalBundleVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentNullException.ThrowIfNull(verifier);
        _bundleDirectory = Path.GetFullPath(bundleDirectory);
        _verifier = verifier;
    }

    public async Task<ApplicationVerification> VerifyAsync(
        CancellationToken cancellationToken)
    {
        var verification = await _verifier
            .VerifyAsync(_bundleDirectory, cancellationToken)
            .ConfigureAwait(false);
        return verification switch
        {
            LegalBundleVerification.Verified =>
                new ApplicationVerification.Verified(),
            LegalBundleVerification.Rejected rejected =>
                new ApplicationVerification.Rejected(
                    rejected.Issues
                        .Select(issue => new ApplicationIssue(
                            MapIssueCode(issue.Code),
                            issue.Subject))
                        .ToArray()),
            _ => throw new InvalidOperationException(
                "The runtime legal verification result is unsupported."),
        };
    }

    private static string MapIssueCode(string code) =>
        code switch
        {
            "legal-bundle-missing" or
            "legal-bundle-payload-missing" => "LEGAL_BUNDLE_MISSING",
            "legal-bundle-payload-unexpected" => "UNAPPROVED_COMPONENT",
            _ => "LEGAL_BUNDLE_HASH_MISMATCH",
        };
}
