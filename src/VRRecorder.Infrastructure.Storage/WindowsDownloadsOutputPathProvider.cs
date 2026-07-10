using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class WindowsDownloadsOutputPathProvider
    : IDefaultOutputPathProvider
{
    private static readonly Guid DownloadsFolderId = Guid.Parse(
        "374de290-123f-4565-9164-39c4925e467b");
    private readonly IWindowsKnownFolderPathApi _knownFolders;

    public WindowsDownloadsOutputPathProvider()
        : this(new ShellWindowsKnownFolderPathApi())
    {
    }

    public WindowsDownloadsOutputPathProvider(
        IWindowsKnownFolderPathApi knownFolders)
    {
        ArgumentNullException.ThrowIfNull(knownFolders);
        _knownFolders = knownFolders;
    }

    public OutputPath GetDefault() =>
        new(_knownFolders.GetPath(DownloadsFolderId));
}
