using System.Diagnostics;
using System.IO;
using VRRecorder.Application.Ports;

namespace VRRecorder.App;

internal sealed class WindowsLegalFolderShell : ILegalFolderShell
{
    public Task OpenFolderAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Opening a Legal Bundle folder is only supported by the Windows host.");
        }

        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The Legal Bundle folder does not exist: {fullPath}");
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            Verb = "open",
        }) ?? throw new InvalidOperationException(
            "The Windows shell did not open the Legal Bundle folder.");
        return Task.CompletedTask;
    }
}
