namespace VRRecorder.Application.Compliance;

public sealed record LegalCatalogComponent(
    string Id,
    string DisplayName,
    string Version,
    string LicenseExpression,
    string Usage,
    string Linkage,
    bool Modified,
    string SourceInformation,
    string LicenseTextPath);

public sealed record LegalCatalogSnapshot(
    string BundleId,
    string ProductVersion,
    IReadOnlyList<LegalCatalogComponent> Components);

public sealed record LegalTextDocument(
    string ComponentId,
    string RelativePath,
    string Text);

public sealed record LegalCatalogIssue(
    string Code,
    string Subject);

public abstract record LegalCatalogReadResult
{
    private LegalCatalogReadResult()
    {
    }

    public sealed record Available(LegalCatalogSnapshot Catalog)
        : LegalCatalogReadResult;

    public sealed record Rejected(IReadOnlyList<LegalCatalogIssue> Issues)
        : LegalCatalogReadResult;
}

public abstract record LegalTextReadResult
{
    private LegalTextReadResult()
    {
    }

    public sealed record Available(LegalTextDocument Document)
        : LegalTextReadResult;

    public sealed record Rejected(IReadOnlyList<LegalCatalogIssue> Issues)
        : LegalTextReadResult;
}
