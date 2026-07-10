namespace VRRecorder.Application.Compliance;

public abstract record LegalFolderOpenResult
{
    private LegalFolderOpenResult()
    {
    }

    public sealed record Opened(string FolderPath) : LegalFolderOpenResult;

    public sealed record Rejected(IReadOnlyList<LegalCatalogIssue> Issues)
        : LegalFolderOpenResult;
}
