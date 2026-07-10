using System.IO;
using System.Net.Http;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Osc;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.App;

internal sealed class ProductionDesktopRecordingRuntimeFactory
    : IDesktopRecordingRuntimeFactory
{
    private const string NativeLibraryFileName = "vrrecorder_native.dll";
    private const string FfprobeFileName = "ffprobe.exe";
    private const string CameraLeaseFileName = "camera-lease.json";
    private const long MaximumEstimatedBytesPerSecond = 11_000_000;
    private static readonly TimeSpan OscQueryConnectTimeout =
        TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OscQueryDiscoveryTimeout =
        TimeSpan.FromSeconds(2);

    public async Task<IDesktopRecordingRuntime> InitializeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RecoverStaleCameraLeaseAsync(cancellationToken);
        await RecoverStaleRecordingsAsync(cancellationToken);

        var nativeLibraryPath = Path.Combine(
            AppContext.BaseDirectory,
            NativeLibraryFileName);
        if (!File.Exists(nativeLibraryPath))
        {
            throw new DesktopRecordingInitializationException(
                "NATIVE_MEDIA_LIBRARY_MISSING",
                $"The required native media library {NativeLibraryFileName} " +
                "is not installed.");
        }

        var ffprobePath = Path.Combine(
            AppContext.BaseDirectory,
            FfprobeFileName);
        if (!File.Exists(ffprobePath))
        {
            throw new DesktopRecordingInitializationException(
                "FFPROBE_EXECUTABLE_MISSING",
                $"The required media validator {FfprobeFileName} is not installed.");
        }

        return ComposeRuntime(nativeLibraryPath, ffprobePath);
    }

    private static DesktopRecordingRuntime ComposeRuntime(
        string nativeLibraryPath,
        string ffprobePath)
    {
        var resources = new List<IDisposable>();
        try
        {
            var settingsPath = new WindowsSettingsPathProvider().GetPath();
            var cameraLeasePath = CameraLeasePath(settingsPath);
            var recordingRights =
                new JsonFileRecordingRightsAcknowledgementStore(
                    RecordingRightsPath(settingsPath),
                    SystemWallClock.Instance);
            resources.Add(recordingRights);
            var diagnosticLog = new RotatingJsonLinesDiagnosticLog(
                LogDirectory(settingsPath));
            resources.Add(diagnosticLog);
            var http = CreateLoopbackHttpInvoker();
            resources.Add(http);
            var cameraLeases = new FileSystemCameraLeaseStore(cameraLeasePath);
            resources.Add(cameraLeases);
            var cameraConnections = CreateCameraConnections(http);

            var spoutSource = new PInvokeSpoutVideoSource(nativeLibraryPath);
            resources.Add(spoutSource);
            var encoderProbe = new PInvokeEncoderProbe(nativeLibraryPath);
            resources.Add(encoderProbe);
            var nativeBackend = new PInvokeNativeRecordingBackend(
                nativeLibraryPath);
            resources.Add(nativeBackend);

            var clock = new SystemMonotonicClock();
            var wallClock = SystemWallClock.Instance;
            var faultStops = new NativeRecordingFaultStopSink();
            var recordingEngine = new NativeRecordingEngine(
                nativeBackend,
                clock,
                faultStops);
            var events = new StructuredRecordingEventSink(
                diagnosticLog,
                wallClock);
            var finalization = new RecordingFileFinalizationUseCase(
                new SameDirectoryAtomicRecordingFileFinalizer(),
                new FfprobeRecordingFileValidator(ffprobePath),
                new FileSystemRecordingRecoveryStore(),
                events);
            var sessions = new ActiveRecordingSessionCoordinator(
                recordingEngine,
                finalization);
            faultStops.Bind(sessions);

            var storage = new FileSystemStorageSpaceProbe();
            var storageMonitor = new RecordingStorageMonitor(
                MaximumEstimatedBytesPerSecond,
                clock,
                storage,
                events,
                sessions);
            var startRecording = new StartRecordingUseCase(
                new SpoutVideoSignalGateway(spoutSource),
                new MonotonicCountdownTimer(clock),
                new FileSystemRecordingFileReservation(),
                wallClock,
                storage,
                new EncoderSelector(encoderProbe),
                recordingEngine,
                sessions,
                storageMonitor,
                new AutoStopScheduler(clock, sessions));
            var lifecycle = new RecordingLifecycleController(
                cameraConnections,
                cameraLeases,
                startRecording,
                sessions,
                events,
                new SystemCameraLeaseIdentitySource(wallClock));
            resources.Add(new RecorderStatusDiagnosticObserver(
                lifecycle,
                diagnosticLog,
                wallClock));

            var legalVerifier = new AuthenticatedLegalBundleVerifier(
                new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
                    typeof(ProductionDesktopRecordingRuntimeFactory).Assembly));
            IDesktopRecordingStartRequestSource requests =
                new SettingsDesktopRecordingStartRequestSource(
                    new JsonFileSettingsStore(settingsPath, wallClock),
                    new WindowsDownloadsOutputPathProvider());
            requests = new LegalBundleMirroringDesktopRecordingStartRequestSource(
                requests,
                new AuthenticatedLegalBundleOutputMirror(
                    AppContext.BaseDirectory,
                    ProductVersion(),
                    legalVerifier));
            requests = new RightsAcknowledgedDesktopRecordingStartRequestSource(
                requests,
                new RecordingRightsGate(recordingRights, wallClock));
            var ownedLifetime = new RecordingRuntimeResourceLifetime(
                [.. resources]);
            return new DesktopRecordingRuntime(
                requests,
                lifecycle,
                sessions,
                ownedLifetime);
        }
        catch
        {
            DisposeResourcesBestEffort(resources);
            throw;
        }
    }

    private static async Task RecoverStaleCameraLeaseAsync(
        CancellationToken cancellationToken)
    {
        using var leases = new FileSystemCameraLeaseStore(CameraLeasePath());
        using var http = CreateLoopbackHttpInvoker();
        var warnings = new StartupCameraRestoreWarningSink();
        var result = await new StaleCameraLeaseRecoveryUseCase(
                leases,
                new SystemProcessCameraLeaseOwnerActivityProbe(),
                CreateCameraConnections(http),
                warnings)
            .ExecuteAsync(cancellationToken);
        EnsureRecoverySucceeded(result, warnings.Warning);
    }

    private static async Task RecoverStaleRecordingsAsync(
        CancellationToken cancellationToken)
    {
        var settings = await new JsonFileSettingsStore(SettingsPath())
            .LoadAsync(cancellationToken);
        var outputPath = new RecordingOutputPathResolver(
                new WindowsDownloadsOutputPathProvider())
            .Resolve(settings.Recording.OutputFolder);
        await new StaleRecordingRecoveryUseCase(
                new FileSystemStaleRecordingCatalog(),
                new FileSystemRecordingRecoveryStore())
            .ExecuteAsync(outputPath.FullPath, cancellationToken);
    }

    private static string CameraLeasePath()
    {
        var settingsPath = SettingsPath();
        return CameraLeasePath(settingsPath);
    }

    private static string SettingsPath() =>
        new WindowsSettingsPathProvider().GetPath();

    private static string CameraLeasePath(string settingsPath)
    {
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(applicationDataDirectory, CameraLeaseFileName);
    }

    private static string ProductVersion() =>
        typeof(ProductionDesktopRecordingRuntimeFactory)
            .Assembly
            .GetName()
            .Version?
            .ToString() ?? "0.0.0.0";

    private static string LogDirectory(string settingsPath)
    {
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(applicationDataDirectory, "logs");
    }

    private static string RecordingRightsPath(string settingsPath)
    {
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(
            applicationDataDirectory,
            "recording-rights.json");
    }

    private static void DisposeResourcesBestEffort(
        List<IDisposable> resources)
    {
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                resources[index].Dispose();
            }
            catch (Exception)
            {
                // Preserve the initialization failure that owns this cleanup.
            }
        }
    }

    private static HttpMessageInvoker CreateLoopbackHttpInvoker() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = OscQueryConnectTimeout,
            UseProxy = false,
        });

    private static VrChatCameraConnectionUseCase CreateCameraConnections(
        HttpMessageInvoker http)
    {
        var discovery = new OscQueryVrChatInstanceDiscovery(
            new WindowsDnsSdOscQueryServiceBrowser(),
            http,
            OscQueryDiscoveryTimeout);
        return new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            new ConfirmedUdpVrChatCameraGatewayFactory(http));
    }

    private static void EnsureRecoverySucceeded(
        StaleCameraLeaseRecoveryResult result,
        CameraRestoreWarning? warning)
    {
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
                    warning?.Failure.Message ??
                    "The stale VRChat camera lease could not be recovered safely.",
                    warning?.Failure);
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
