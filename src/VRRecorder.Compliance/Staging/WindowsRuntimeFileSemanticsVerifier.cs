using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Compliance.Staging;

internal interface IWindowsRuntimeFileSemanticsVerifier
{
    void VerifyRegularFile(string path);
}

internal sealed class WindowsRuntimeFileSemanticsVerifier
    : IWindowsRuntimeFileSemanticsVerifier
{
    private const int ErrorHandleEof = 38;
    private const int FindStreamInfoStandard = 0;
    private const int StreamNameCapacity = 296;

    public static WindowsRuntimeFileSemanticsVerifier Instance { get; } =
        new();

    private WindowsRuntimeFileSemanticsVerifier()
    {
    }

    public void VerifyRegularFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || file.LinkTarget is not null ||
            file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException(
                $"Windows runtime files must be regular and unlinked: {path}");
        }

        if (OperatingSystem.IsWindows())
        {
            VerifyNoNamedDataStreams(path);
        }
    }

    private static void VerifyNoNamedDataStreams(string path)
    {
        using var handle = FindFirstStream(
            path,
            FindStreamInfoStandard,
            out var stream,
            flags: 0);
        if (handle.IsInvalid)
        {
            throw StreamEnumerationFailure(path, Marshal.GetLastWin32Error());
        }

        if (!string.Equals(
                stream.StreamName,
                "::$DATA",
                StringComparison.Ordinal))
        {
            throw NamedStream(path);
        }

        if (FindNextStream(handle, out _))
        {
            throw NamedStream(path);
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorHandleEof)
        {
            throw StreamEnumerationFailure(path, error);
        }
    }

    private static InvalidDataException NamedStream(string path) => new(
        $"Windows runtime files cannot contain named data streams: {path}");

    private static InvalidDataException StreamEnumerationFailure(
        string path,
        int error) => new(
        $"Windows runtime file streams could not be enumerated: {path}",
        new Win32Exception(error));

    [DllImport(
        "kernel32.dll",
        EntryPoint = "FindFirstStreamW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern FindStreamSafeHandle FindFirstStream(
        string fileName,
        int infoLevel,
        out FindStreamData findStreamData,
        int flags);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "FindNextStreamW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStream(
        FindStreamSafeHandle findStream,
        out FindStreamData findStreamData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(nint findFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = StreamNameCapacity)]
        public string StreamName;
    }

    private sealed class FindStreamSafeHandle()
        : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => FindClose(handle);
    }
}
