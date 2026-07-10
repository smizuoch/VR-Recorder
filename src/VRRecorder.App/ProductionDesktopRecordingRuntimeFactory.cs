using System.IO;
using System.Net.Http;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Infrastructure.Osc;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.App;

internal sealed class ProductionDesktopRecordingRuntimeFactory
    : IDesktopRecordingRuntimeFactory
{
    private const string NativeLibraryFileName = "vrrecorder_native.dll";
    private const string CameraLeaseFileName = "camera-lease.json";
    private static readonly TimeSpan OscQueryDiscoveryTimeout =
        TimeSpan.FromSeconds(2);

    public async Task<IDesktopRecordingRuntime> InitializeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RecoverStaleCameraLeaseAsync(cancellationToken);

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
        throw failure;
    }

    private static async Task RecoverStaleCameraLeaseAsync(
        CancellationToken cancellationToken)
    {
        var settingsPath = new WindowsSettingsPathProvider().GetPath();
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        var leasePath = Path.Combine(
            applicationDataDirectory,
            CameraLeaseFileName);
        using var leases = new FileSystemCameraLeaseStore(leasePath);
        using var http = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(1),
            UseProxy = false,
        });
        var discovery = new OscQueryVrChatInstanceDiscovery(
            new WindowsDnsSdOscQueryServiceBrowser(),
            http,
            OscQueryDiscoveryTimeout);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            new ConfirmedUdpVrChatCameraGatewayFactory(http));
        var warnings = new StartupCameraRestoreWarningSink();
        var result = await new StaleCameraLeaseRecoveryUseCase(
                leases,
                new SystemProcessCameraLeaseOwnerActivityProbe(),
                connections,
                warnings)
            .ExecuteAsync(cancellationToken);

        switch (result)
        {
            case StaleCameraLeaseRecoveryResult.NoLease:
            case StaleCameraLeaseRecoveryResult.Restored:
                return;
            case StaleCameraLeaseRecoveryResult.OwnerStillActive:
                throw new DesktopRecordingInitializationException(
                    "CAMERA_LEASE_OWNER_ACTIVE",
                    "Another live VR-Recorder process owns the persisted camera lease.");
            case StaleCameraLeaseRecoveryResult.Failed failed:
                throw new DesktopRecordingInitializationException(
                    failed.Code,
                    warnings.Warning?.Failure.Message ??
                    "The stale VRChat camera lease could not be recovered safely.",
                    warnings.Warning?.Failure);
            default:
                throw new DesktopRecordingInitializationException(
                    "CAMERA_LEASE_RECOVERY_RESULT_INVALID",
                    "Camera lease recovery returned an unsupported result.");
        }
    }

    private sealed class StartupCameraRestoreWarningSink
        : ICameraRestoreWarningSink
    {
        public CameraRestoreWarning? Warning { get; private set; }

        public Task PublishAsync(
            CameraRestoreWarning warning,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(warning);
            cancellationToken.ThrowIfCancellationRequested();
            Warning = warning;
            return Task.CompletedTask;
        }
    }
}
