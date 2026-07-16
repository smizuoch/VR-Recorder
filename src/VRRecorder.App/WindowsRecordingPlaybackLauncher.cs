using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.App;

internal sealed class WindowsRecordingPlaybackLauncher
    : IRecordingPlaybackLauncher
{
    public Task<bool> StartAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() ||
            !Path.IsPathFullyQualified(recording.FinalPath) ||
            !string.Equals(
                Path.GetExtension(recording.FinalPath),
                ".mp4",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(recording.FinalPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                recording.FinalPath)
            {
                UseShellExecute = true,
            });
            return Task.FromResult(process is not null);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }
}
