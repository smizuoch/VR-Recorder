namespace VRRecorder.Application.Compliance;

public enum LegalDocumentKind
{
    License,
    Notice,
    Copyright,
    Attribution,
    AssetManifest,
}

public sealed record LegalDocumentReference(
    LegalDocumentKind Kind,
    string RelativePath);

public sealed record LegalCatalogComponent(
    string Id,
    string DisplayName,
    string Version,
    string LicenseExpression,
    string Usage,
    string Linkage,
    bool Modified,
    string SourceInformation,
    string CopyrightNotice,
    IReadOnlyList<LegalDocumentReference> LegalDocuments)
{
    public LegalCatalogComponent(
        string id,
        string displayName,
        string version,
        string licenseExpression,
        string usage,
        string linkage,
        bool modified,
        string sourceInformation,
        string licenseTextPath)
        : this(
            id,
            displayName,
            version,
            licenseExpression,
            usage,
            linkage,
            modified,
            sourceInformation,
            string.Empty,
            [new LegalDocumentReference(
                LegalDocumentKind.License,
                licenseTextPath)])
    {
    }

    public string LicenseTextPath => LegalDocuments
        .Single(reference => reference.Kind == LegalDocumentKind.License)
        .RelativePath;
}

public sealed record LegalCatalogSnapshot(
    string BundleId,
    string ProductVersion,
    string ManifestSha256,
    IReadOnlyList<LegalCatalogComponent> Components)
{
    public LegalCatalogSnapshot(
        string bundleId,
        string productVersion,
        IReadOnlyList<LegalCatalogComponent> components)
        : this(bundleId, productVersion, string.Empty, components)
    {
    }
}

public sealed record LegalTextDocument(
    string ComponentId,
    LegalDocumentReference Reference,
    string Text)
{
    public LegalTextDocument(
        string componentId,
        string relativePath,
        string text)
        : this(
            componentId,
            new LegalDocumentReference(
                LegalDocumentKind.License,
                relativePath),
            text)
    {
    }

    public string RelativePath => Reference.RelativePath;
}

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
