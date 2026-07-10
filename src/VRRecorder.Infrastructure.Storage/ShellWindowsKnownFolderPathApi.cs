using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Storage;

public sealed partial class ShellWindowsKnownFolderPathApi
    : IWindowsKnownFolderPathApi
{
    public string GetPath(Guid folderId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Known Folders are available only on Windows.");
        }

        var result = SHGetKnownFolderPath(
            in folderId,
            flags: 0,
            token: 0,
            out var pathPointer);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return Marshal.PtrToStringUni(pathPointer) ??
                   throw new InvalidOperationException(
                       "The Windows shell returned an empty Known Folder path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(
        in Guid folderId,
        uint flags,
        nint token,
        out nint path);
}
