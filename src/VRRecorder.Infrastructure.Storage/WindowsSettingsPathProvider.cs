namespace VRRecorder.Infrastructure.Storage;

public sealed class WindowsSettingsPathProvider
{
    private static readonly Guid LocalAppDataFolderId = Guid.Parse(
        "f1b32785-6fba-4fcf-9d55-7b8e7f157091");
    private readonly IWindowsKnownFolderPathApi _knownFolders;

    public WindowsSettingsPathProvider()
        : this(new ShellWindowsKnownFolderPathApi())
    {
    }

    public WindowsSettingsPathProvider(
        IWindowsKnownFolderPathApi knownFolders)
    {
        ArgumentNullException.ThrowIfNull(knownFolders);
        _knownFolders = knownFolders;
    }

    public string GetPath() =>
        Path.GetFullPath(Path.Combine(
            _knownFolders.GetPath(LocalAppDataFolderId),
            "VR-Recorder",
            "settings.json"));
}
