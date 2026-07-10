using System.IO;
using VRRecorder.Application.Desktop;

namespace VRRecorder.App;

internal sealed class ProductionDesktopRecordingRuntimeFactory
    : IDesktopRecordingRuntimeFactory
{
    private const string NativeLibraryFileName = "vrrecorder_native.dll";

    public Task<IDesktopRecordingRuntime> InitializeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nativeLibraryPath = Path.Combine(
            AppContext.BaseDirectory,
            NativeLibraryFileName);
        var failure = File.Exists(nativeLibraryPath)
            ? new DesktopRecordingInitializationException(
                "RECORDING_SERVICE_COMPOSITION_UNAVAILABLE",
                "The installed native media library cannot be activated " +
                "until the production Spout, audio, and encoder services " +
                "are composed.")
            : new DesktopRecordingInitializationException(
                "NATIVE_MEDIA_LIBRARY_MISSING",
                $"The required native media library {NativeLibraryFileName} " +
                "is not installed.");
        return Task.FromException<IDesktopRecordingRuntime>(failure);
    }
}
