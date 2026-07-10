namespace VRRecorder.Compliance.Runtime;

public sealed record LegalBundleIdentity(
    string BundleId,
    string ManifestSha256);

public abstract record LegalBundleVerification
{
    private LegalBundleVerification()
    {
    }

    public sealed record Verified(LegalBundleIdentity Identity)
        : LegalBundleVerification;

    public sealed record Rejected(IReadOnlyList<ComplianceIssue> Issues)
        : LegalBundleVerification;
}
