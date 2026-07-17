namespace VRRecorder.Compliance.Runtime;

public sealed record LegalBundleIdentity(
    string BundleId,
    string ManifestSha256,
    string? ProductVersion = null);

public abstract record LegalBundleVerification
{
    private LegalBundleVerification()
    {
    }

    public sealed record Verified(LegalBundleIdentity Identity)
        : LegalBundleVerification
    {
        internal IReadOnlyList<string> AuthenticatedRelativePaths
        {
            get;
            init;
        } = [];
    }

    public sealed record Rejected(IReadOnlyList<ComplianceIssue> Issues)
        : LegalBundleVerification;
}
