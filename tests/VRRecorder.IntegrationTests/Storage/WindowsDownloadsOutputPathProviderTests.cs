using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class WindowsDownloadsOutputPathProviderTests
{
    [Fact]
    public void ResolvesDownloadsThroughExactKnownFolderIdentifier()
    {
        var downloads = Path.Combine(
            Path.GetTempPath(),
            "localized-downloads-name");
        var knownFolders = new CapturingKnownFolderPathApi(downloads);
        var provider = new WindowsDownloadsOutputPathProvider(knownFolders);

        var outputPath = provider.GetDefault();

        Assert.Equal(Path.GetFullPath(downloads), outputPath.FullPath);
        Assert.Equal(
            Guid.Parse("374de290-123f-4565-9164-39c4925e467b"),
            knownFolders.RequestedFolderId);
    }

    private sealed class CapturingKnownFolderPathApi
        : IWindowsKnownFolderPathApi
    {
        private readonly string _path;

        public CapturingKnownFolderPathApi(string path)
        {
            _path = path;
        }

        public Guid? RequestedFolderId { get; private set; }

        public string GetPath(Guid folderId)
        {
            RequestedFolderId = folderId;
            return _path;
        }
    }
}
