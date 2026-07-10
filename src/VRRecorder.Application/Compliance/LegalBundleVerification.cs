namespace VRRecorder.Application.Compliance;

public sealed record LegalBundleIssue(string Code, string Subject);

public abstract record LegalBundleVerification
{
    private LegalBundleVerification()
    {
    }

    public sealed record Verified : LegalBundleVerification;

    public sealed record Rejected(IReadOnlyList<LegalBundleIssue> Issues)
        : LegalBundleVerification;
}
