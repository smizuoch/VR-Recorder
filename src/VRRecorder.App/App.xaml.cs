using System.Globalization;
using System.IO;
using System.Net.Http;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.Storage;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Osc;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.App;

public partial class App
    : System.Windows.Application, IDisposable, IUiLocaleApplier
{
    private const string LocaleArgumentPrefix = "--ui-locale=";
    private const string NativeLibraryFileName = "vrrecorder_native.dll";
    private readonly object _steamVrInputGate = new();
    private readonly DesktopTrayUiController _trayUi = new();
    private readonly DesktopRecordingNotificationHub _recordingNotifications =
        new DesktopRecordingNotificationHub();
    private readonly DesktopRecordingCommandHost _recordingHost;
    private readonly IUiCommandDispatcher _uiCommands;
    private readonly RecordingInputDispatcher _recordingInputs;
    private readonly DesktopDiagnosticsController _diagnosticsController;
    private readonly DesktopRecordingSettingsController _recordingSettings;
    private readonly JsonFileSettingsStore _settings;
    private readonly AuthenticatedLegalBundleVerifier _legalVerifier;
    private readonly DesktopLegalController _legalController;
    private readonly JsonFileRecordingRightsAcknowledgementStore
        _recordingRightsStore;
    private readonly RecordingRightsGate _recordingRightsGate;
    private readonly JsonFileFirstRunSetupStore _firstRunSetupStore;
    private readonly FirstRunSetupController _firstRunSetup;
    private readonly FirstRunSetupUiController _firstRunSetupUi;
    private readonly FirstRunSetupVerificationController
        _firstRunSetupVerification;
    private readonly HttpMessageInvoker _setupOscHttp;
    private readonly CancellationTokenSource _steamVrInputLifetime = new();
    private readonly Lazy<ISteamVrInputRuntime> _steamVrInputRuntime;
    private readonly Lazy<NativeSteamVrOverlayLifecycle>
        _steamVrOverlayLifecycle;
    private readonly Lazy<WristOverlayPlacementCoordinator>
        _wristOverlayPlacement;
    private Task? _steamVrInputTask;
    private IDisposable? _trayNotificationSubscription;
    private IDisposable? _trayStatusSubscription;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayStatusMenuItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayToggleMenuItem;
    private bool _recordingRightsAuthorized;
    private bool _exitRequested;
    private int _disposeStarted;

    public App()
    {
        var settingsPath = new WindowsSettingsPathProvider().GetPath();
        var settingsStore = new JsonFileSettingsStore(
            settingsPath,
            SystemWallClock.Instance);
        _settings = settingsStore;
        _legalVerifier = new AuthenticatedLegalBundleVerifier(
            new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
                typeof(App).Assembly));
        var outputPaths = new RecordingOutputPathResolver(
            new WindowsDownloadsOutputPathProvider());
        var legalOutputMirror = new AuthenticatedLegalBundleOutputMirror(
            AppContext.BaseDirectory,
            ProductVersion(),
            _legalVerifier);
        var legalCatalogReader = new AuthenticatedLegalCatalogReader(
            AppContext.BaseDirectory,
            _legalVerifier,
            LegalBundleVerificationScope.InstallRoot);
        _recordingSettings = new DesktopRecordingSettingsController(
            settingsStore,
            outputPaths,
            legalOutputMirror,
            new WindowsAudioEndpointCatalog(),
            this);
        _diagnosticsController = new DesktopDiagnosticsController(
            new PrivacySafeDiagnosticBundleExporter(
                LogDirectory(settingsPath)));
        _recordingHost = new DesktopRecordingCommandHost(
            new ProductionDesktopRecordingRuntimeFactory(
                settingsStore,
                _recordingNotifications));
        _recordingRightsStore =
            new JsonFileRecordingRightsAcknowledgementStore(
                RecordingRightsPath(settingsPath));
        _recordingRightsGate = new RecordingRightsGate(
            _recordingRightsStore,
            SystemWallClock.Instance);
        _firstRunSetupStore = new JsonFileFirstRunSetupStore(
            FirstRunSetupPath(settingsPath));
        _firstRunSetup = new FirstRunSetupController(_firstRunSetupStore);
        _firstRunSetupUi = new FirstRunSetupUiController(_firstRunSetup);
        _setupOscHttp = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            UseProxy = false,
        });
        var setupOscDiscovery = new OscQueryVrChatInstanceDiscovery(
            new WindowsDnsSdOscQueryServiceBrowser(),
            _setupOscHttp,
            TimeSpan.FromSeconds(3));
        _steamVrInputRuntime = new Lazy<ISteamVrInputRuntime>(
            () => new NativeSteamVrInputRuntime(
                Path.Combine(
                    AppContext.BaseDirectory,
                    NativeLibraryFileName),
                AppContext.BaseDirectory),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _steamVrOverlayLifecycle =
            new Lazy<NativeSteamVrOverlayLifecycle>(
                () => new NativeSteamVrOverlayLifecycle(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        NativeLibraryFileName),
                    AppContext.BaseDirectory),
                LazyThreadSafetyMode.ExecutionAndPublication);
        _wristOverlayPlacement =
            new Lazy<WristOverlayPlacementCoordinator>(
                () => new WristOverlayPlacementCoordinator(
                    settingsStore,
                    _steamVrOverlayLifecycle.Value),
                LazyThreadSafetyMode.ExecutionAndPublication);
        var setupProbe = new FirstRunSetupProbeRouter(
            new Dictionary<FirstRunSetupStep, IFirstRunSetupProbe>
            {
                [FirstRunSetupStep.SteamVrDetection] =
                    new WindowsSteamVrInstallationProbe(),
                [FirstRunSetupStep.VrChatOscDetection] =
                    new VrChatOscFirstRunSetupProbe(setupOscDiscovery),
                [FirstRunSetupStep.CameraOscEndpoint] =
                    new CameraOscEndpointFirstRunSetupProbe(
                        setupOscDiscovery,
                        new ConfirmedUdpVrChatCameraGatewayFactory(
                            _setupOscHttp)),
                [FirstRunSetupStep.MicrophonePrivacyAndDevice] =
                    new MicrophonePrivacyAndDeviceFirstRunSetupProbe(
                        settingsStore,
                        new WindowsAudioEndpointCatalog(),
                        new WindowsMicrophonePrivacyAccess()),
                [FirstRunSetupStep.EncoderSelfTest] =
                    new EncoderSelfTestFirstRunSetupProbe(
                        settingsStore,
                        new TransientEncoderProbe(Path.Combine(
                            AppContext.BaseDirectory,
                            NativeLibraryFileName))),
                [FirstRunSetupStep.SteamVrActionBinding] =
                    new SteamVrActionBindingFirstRunSetupProbe(
                        () => _steamVrInputRuntime.Value),
                [FirstRunSetupStep.LegalBundleVerification] =
                    new LegalBundleVerificationFirstRunSetupProbe(
                        settingsStore,
                        outputPaths,
                        legalOutputMirror),
                [FirstRunSetupStep.OfflineLegalAccess] =
                    new OfflineLegalAccessFirstRunSetupProbe(
                        legalCatalogReader),
                [FirstRunSetupStep.LocalizationAccessibility] =
                    new LocalizationAccessibilityFirstRunSetupProbe(
                        legalCatalogReader),
                [FirstRunSetupStep.DesignAssetConformance] =
                    new DesignAssetConformanceFirstRunSetupProbe(
                        legalCatalogReader),
            });
        _firstRunSetupVerification =
            new FirstRunSetupVerificationController(
                _firstRunSetup,
                setupProbe);
        _legalController = new DesktopLegalController(
            legalCatalogReader,
            new AuthenticatedLegalBundleFolderOpener(
                AppContext.BaseDirectory,
                AppContext.BaseDirectory,
                _legalVerifier,
                new WindowsLegalFolderShell(),
                LegalBundleVerificationScope.InstallRoot),
            _recordingHost);
        _uiCommands = new RecordingUiCommandDispatcher(
            (_, cancellationToken) =>
                _recordingHost.ToggleAsync(cancellationToken),
            (_, cancellationToken) =>
                _recordingHost.ExecuteAudioCommandAsync(
                    RecordingAudioCommand.ToggleMicrophone,
                    cancellationToken),
            (_, cancellationToken) =>
                _recordingHost.ExecuteAudioCommandAsync(
                    RecordingAudioCommand.ToggleMuteAll,
                    cancellationToken));
        _recordingInputs = new RecordingInputDispatcher(_uiCommands);
    }

    internal static RecordingInputDispatcher RecordingInputs =>
        ((App)Current)._recordingInputs;

    internal static IUiCommandDispatcher UiCommands =>
        ((App)Current)._uiCommands;

    internal static DesktopLegalController LegalController =>
        ((App)Current)._legalController;

    internal static DesktopRecordingSettingsController RecordingSettings =>
        ((App)Current)._recordingSettings;

    internal static DesktopDiagnosticsController DiagnosticsController =>
        ((App)Current)._diagnosticsController;

    internal static FirstRunSetupController FirstRunSetup =>
        ((App)Current)._firstRunSetup;

    internal static FirstRunSetupUiController FirstRunSetupUi =>
        ((App)Current)._firstRunSetupUi;

    internal static FirstRunSetupVerificationController
        FirstRunSetupVerification =>
        ((App)Current)._firstRunSetupVerification;

    internal static IRecorderStatusSource RecordingStatuses =>
        ((App)Current)._recordingHost;

    internal static DesktopRecordingNotificationHub RecordingNotifications =>
        ((App)Current)._recordingNotifications;

    internal static bool IsExitRequested =>
        ((App)Current)._exitRequested;

    internal static async Task<bool> IsRecordingRightsAcknowledgedAsync(
        CancellationToken cancellationToken)
    {
        var app = (App)Current;
        var acknowledged = await app._recordingRightsGate
            .IsAcknowledgedAsync(cancellationToken);
        if (acknowledged)
        {
            app.AuthorizeRecordingCommands();
        }

        return acknowledged;
    }

    internal static async Task AcknowledgeRecordingRightsAsync(
        CancellationToken cancellationToken)
    {
        var app = (App)Current;
        await app._recordingRightsGate.AcknowledgeAsync(cancellationToken);
        app.AuthorizeRecordingCommands();
    }

    internal static void ExitAfterRightsDeclined() =>
        ((App)Current).RequestExit();

    internal static async Task<DesktopRecordingHostActivation>
        ActivateRecordingHostAsync(
            RecorderStartupResult startup,
            CancellationToken cancellationToken)
    {
        var app = (App)Current;
        var activation = await app._recordingHost.ActivateAsync(
                startup,
                cancellationToken)
            .ConfigureAwait(true);
        if (activation.State == DesktopRecordingHostState.Ready)
        {
            app.StartSteamVrInput();
        }

        return activation;
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var localeName = e.Args.FirstOrDefault(argument =>
            argument.StartsWith(
                LocaleArgumentPrefix,
                StringComparison.OrdinalIgnoreCase));
        var configuredLocale = _settings
            .LoadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .UiLocale;
        SelectLocalizedResources(localeName is null
            ? LocaleName(configuredLocale)
            : localeName[LocaleArgumentPrefix.Length..]);
        base.OnStartup(e);
        InitializeTrayIcon();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _exitRequested = true;
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            _exitRequested = true;
            if (_trayStatusSubscription is not null)
            {
                _trayStatusSubscription.Dispose();
                _trayStatusSubscription = null;
            }

            if (_trayNotificationSubscription is not null)
            {
                _trayNotificationSubscription.Dispose();
                _trayNotificationSubscription = null;
            }

            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;
            _steamVrInputLifetime.Cancel();
            Task? steamVrInputTask;
            lock (_steamVrInputGate)
            {
                steamVrInputTask = _steamVrInputTask;
            }

            steamVrInputTask?.GetAwaiter().GetResult();
            if (_wristOverlayPlacement.IsValueCreated)
            {
                _wristOverlayPlacement.Value.Dispose();
            }
            if (_steamVrOverlayLifecycle.IsValueCreated)
            {
                _steamVrOverlayLifecycle.Value.Dispose();
            }
        }
        finally
        {
            _steamVrInputLifetime.Dispose();
            try
            {
                _recordingHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                try
                {
                    _recordingNotifications.Dispose();
                }
                finally
                {
                    try
                    {
                        _recordingRightsStore.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            _firstRunSetupStore.Dispose();
                        }
                        finally
                        {
                            _setupOscHttp.Dispose();
                        }
                    }
                }
            }
        }

        GC.SuppressFinalize(this);
    }

    private void InitializeTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var statusItem = new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_State_Warning"))
        {
            Enabled = false,
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_Show_Label"),
            image: null,
            (_, _) => ShowMainWindow()));
        var toggleItem = new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_Toggle_Label"),
            image: null,
            async (_, _) => await DispatchTrayRecordingAsync());
        menu.Items.Add(toggleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_Legal_Label"),
            image: null,
            (_, _) => ShowLegalWindow()));
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_LicenseFolder_Label"),
            image: null,
            async (_, _) => await OpenLicenseFolderFromTrayAsync()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(
            LocalizedString("Tray_Exit_Label"),
            image: null,
            (_, _) => RequestExit()));

        var icon = new System.Windows.Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = System.Drawing.SystemIcons.Application,
            Text = LocalizedString("Tray_State_Warning"),
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowMainWindow();
        _trayMenu = menu;
        _trayIcon = icon;
        _trayStatusMenuItem = statusItem;
        _trayToggleMenuItem = toggleItem;
        _trayStatusSubscription = _recordingHost.Subscribe(
            OnTrayStatusChanged);
        _trayNotificationSubscription = _recordingNotifications.Subscribe(
            OnTrayRecordingNotification);
    }

    private void OnTrayStatusChanged(RecorderStatusSnapshot status)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyTrayStatus(status);
            return;
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(() => ApplyTrayStatus(status));
        }
    }

    private void ApplyTrayStatus(RecorderStatusSnapshot status)
    {
        var update = _trayUi.Apply(status);
        if (update is not null)
        {
            ApplyTraySnapshot(update);
        }
    }

    private void OnTrayRecordingNotification(
        DesktopRecordingNotification notification)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyTrayRecordingNotification(notification);
            return;
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(() =>
                ApplyTrayRecordingNotification(notification));
        }
    }

    private void ApplyTrayRecordingNotification(
        DesktopRecordingNotification notification)
    {
        if (_trayIcon is null)
        {
            return;
        }

        switch (notification)
        {
            case DesktopRecordingNotification.Saved saved:
                _trayIcon.BalloonTipTitle = LocalizedString(
                    "Recording_Notification_Saved_Title");
                _trayIcon.BalloonTipText = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizedString("Recording_Notification_Saved_Format"),
                    saved.Recording.FinalPath);
                _trayIcon.BalloonTipIcon =
                    System.Windows.Forms.ToolTipIcon.Info;
                break;
            case DesktopRecordingNotification.CameraWarning:
                _trayIcon.BalloonTipTitle = LocalizedString(
                    "Recording_Notification_CameraRestoreWarning_Title");
                _trayIcon.BalloonTipText = LocalizedString(
                    "Recording_Notification_CameraRestoreWarning");
                _trayIcon.BalloonTipIcon =
                    System.Windows.Forms.ToolTipIcon.Warning;
                break;
            case DesktopRecordingNotification.AudioWarning audioWarning:
                _trayIcon.BalloonTipTitle = LocalizedString(
                    "Recording_Notification_Audio_Warning_Title");
                _trayIcon.BalloonTipText = LocalizedString(
                    audioWarning.Warning.Input switch
                    {
                        Domain.Audio.AudioInput.Desktop =>
                            "Recording_Notification_Audio_DesktopUnavailable",
                        Domain.Audio.AudioInput.Microphone =>
                            "Recording_Notification_Audio_MicrophoneUnavailable",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(notification),
                            audioWarning.Warning.Input,
                            "The unavailable audio input is unsupported."),
                    });
                _trayIcon.BalloonTipIcon =
                    System.Windows.Forms.ToolTipIcon.Warning;
                break;
            case DesktopRecordingNotification.AudioRecovered audioRecovered:
                _trayIcon.BalloonTipTitle = LocalizedString(
                    "Recording_Notification_Audio_Recovered_Title");
                _trayIcon.BalloonTipText = LocalizedString(
                    audioRecovered.Recovery.Input switch
                    {
                        Domain.Audio.AudioInput.Desktop =>
                            "Recording_Notification_Audio_DesktopRecovered",
                        Domain.Audio.AudioInput.Microphone =>
                            "Recording_Notification_Audio_MicrophoneRecovered",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(notification),
                            audioRecovered.Recovery.Input,
                            "The recovered audio input is unsupported."),
                    });
                _trayIcon.BalloonTipIcon =
                    System.Windows.Forms.ToolTipIcon.Info;
                break;
        }

        _trayIcon.ShowBalloonTip(5000);
    }

    private void ApplyTraySnapshot(DesktopTrayUiSnapshot update)
    {
        if (
            _trayStatusMenuItem is null ||
            _trayToggleMenuItem is null ||
            _trayIcon is null)
        {
            return;
        }

        var stateLabel = LocalizedString(update.StateLabelResourceKey);
        _trayStatusMenuItem.Text = stateLabel;
        _trayToggleMenuItem.Text = LocalizedString(
            update.ActionLabelResourceKey);
        _trayToggleMenuItem.Enabled =
            _recordingRightsAuthorized && update.IsActionEnabled;
        _trayIcon.Text = stateLabel;
    }

    private void AuthorizeRecordingCommands()
    {
        _recordingRightsAuthorized = true;
        RefreshTrayStatus();
    }

    private void RefreshTrayStatus()
    {
        var current = _trayUi.Current;
        if (current is not null)
        {
            ApplyTraySnapshot(current);
        }
    }

    private string LocalizedString(string resourceKey) =>
        Resources[resourceKey] as string ?? throw new InvalidOperationException(
            $"The localized resource {resourceKey} is missing.");

    private void ShowMainWindow()
    {
        if (MainWindow is not VRRecorder.App.MainWindow window)
        {
            return;
        }

        window.Show();
        window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

    private void ShowLegalWindow()
    {
        ShowMainWindow();
        if (MainWindow is VRRecorder.App.MainWindow window)
        {
            window.OpenLegalWindow();
        }
    }

    private async Task DispatchTrayRecordingAsync()
    {
        try
        {
            await _recordingInputs
                .DispatchAsync(
                    UiActivationKind.DesktopTray,
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Tray recording command is unavailable: {0}",
                exception.Message);
        }
    }

    private async Task OpenLicenseFolderFromTrayAsync()
    {
        try
        {
            await _legalController
                .OpenLicenseFolderAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "The license folder could not be opened: {0}",
                exception.Message);
        }
    }

    private void RequestExit()
    {
        _exitRequested = true;
        Shutdown();
    }

    private static string RecordingRightsPath(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(
            applicationDataDirectory,
            "recording-rights.json");
    }

    private static string FirstRunSetupPath(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(
            applicationDataDirectory,
            "first-run-setup.json");
    }

    private static string LogDirectory(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var applicationDataDirectory = Path.GetDirectoryName(settingsPath) ??
                                       throw new InvalidOperationException(
                                           "The settings path has no parent directory.");
        return Path.Combine(applicationDataDirectory, "logs");
    }

    private static string ProductVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private void StartSteamVrInput()
    {
        lock (_steamVrInputGate)
        {
            if (_steamVrInputTask is not null)
            {
                return;
            }

            _steamVrInputTask = Task.Run(RunSteamVrInputAsync);
        }
    }

    private async Task RunSteamVrInputAsync()
    {
        using var inputLifetime = CancellationTokenSource
            .CreateLinkedTokenSource(_steamVrInputLifetime.Token);
        var microphoneInput = Task.CompletedTask;
        var recenterInput = Task.CompletedTask;
        try
        {
            var runtime = _steamVrInputRuntime.Value;
            microphoneInput = RunOptionalSteamVrMicrophoneInputAsync(
                runtime,
                inputLifetime.Token);
            recenterInput = RunOptionalSteamVrRecenterInputAsync(
                runtime,
                inputLifetime.Token);
            await new SteamVrRecordingInputAdapter(runtime, _recordingInputs)
                .RunAsync(inputLifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _steamVrInputLifetime.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception) when (
            exception is SteamVrInputException or
                FileNotFoundException or
                DllNotFoundException or
                BadImageFormatException or
                IOException or
                UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "SteamVR recording input is unavailable: {0}",
                exception.Message);
        }
        finally
        {
            await inputLifetime.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(microphoneInput, recenterInput)
                .ConfigureAwait(false);
        }
    }

    private async Task RunOptionalSteamVrMicrophoneInputAsync(
        ISteamVrInputRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            await new SteamVrMicrophoneInputAdapter(runtime, _uiCommands)
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Normal recording-input shutdown.
        }
        catch (Exception exception) when (
            exception is SteamVrInputException or
                FileNotFoundException or
                DllNotFoundException or
                BadImageFormatException or
                IOException or
                UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "SteamVR microphone input is unavailable: {0}",
                exception.Message);
        }
    }

    private async Task RunOptionalSteamVrRecenterInputAsync(
        ISteamVrInputRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            await new SteamVrOverlayPlacementInputAdapter(
                    runtime,
                    _wristOverlayPlacement.Value)
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Normal placement-input shutdown.
        }
        catch (Exception exception) when (
            exception is SteamVrInputException or
                SteamVrOverlayException or
                FileNotFoundException or
                DllNotFoundException or
                BadImageFormatException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "SteamVR recenter input is unavailable: {0}",
                exception.Message);
        }
    }

    internal static async Task<RecorderStartupResult> VerifyStartupAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var gateway = new RuntimeLegalBundleVerificationGateway(
                AppContext.BaseDirectory,
                ((App)Current)._legalVerifier,
                LegalBundleVerificationScope.InstallRoot);
            return await new RecorderStartupUseCase(gateway)
                .ExecuteAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new RecorderStartupResult(
                RecorderState.ComplianceFault,
                [new LegalBundleIssue("LEGAL_BUNDLE_MISSING", "install-directory")]);
        }
    }

    private void SelectLocalizedResources(string localeName)
    {
        var (stringsPath, layoutPath) = localeName.ToLowerInvariant() switch
        {
            "qps-ploc" => (
                "Resources/Strings.qps-ploc.xaml",
                "Resources/Layout.ltr.xaml"),
            "qps-plocm" => (
                "Resources/Strings.qps-plocm.xaml",
                "Resources/Layout.rtl.xaml"),
            var name when name == "ja" || name.StartsWith("ja-", StringComparison.Ordinal) =>
                ("Resources/Strings.ja-JP.xaml", "Resources/Layout.ltr.xaml"),
            _ => ("Resources/Strings.en-US.xaml", "Resources/Layout.ltr.xaml"),
        };
        ReplaceMergedResource("Strings.", stringsPath);
        ReplaceMergedResource("Layout.", layoutPath);
    }

    void IUiLocaleApplier.Apply(UiLocale locale) =>
        SelectLocalizedResources(LocaleName(locale));

    private static string LocaleName(UiLocale locale) => locale switch
    {
        UiLocale.System => CultureInfo.CurrentUICulture.Name,
        UiLocale.English => "en-US",
        UiLocale.Japanese => "ja-JP",
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
    };

    private void ReplaceMergedResource(string filePrefix, string resourcePath)
    {
        var resource = Resources.MergedDictionaries.Single(dictionary =>
            Path.GetFileName(dictionary.Source?.OriginalString ?? string.Empty)
                .StartsWith(filePrefix, StringComparison.Ordinal));
        resource.Source = new Uri(resourcePath, UriKind.Relative);
    }

}
