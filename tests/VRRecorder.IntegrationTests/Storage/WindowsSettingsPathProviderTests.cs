using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class WindowsSettingsPathProviderTests
{
    [Fact]
    public void ResolvesSettingsUnderExactLocalAppDataKnownFolder()
    {
        var localAppData = Path.Combine(
            Path.GetTempPath(),
            "localized-local-app-data-name");
        var knownFolders = new CapturingKnownFolderPathApi(localAppData);
        var provider = new WindowsSettingsPathProvider(knownFolders);

        var settingsPath = provider.GetPath();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                localAppData,
                "VR-Recorder",
                "settings.json")),
            settingsPath);
        Assert.Equal(
            Guid.Parse("f1b32785-6fba-4fcf-9d55-7b8e7f157091"),
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
