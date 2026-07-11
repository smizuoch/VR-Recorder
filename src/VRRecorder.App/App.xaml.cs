using System.Globalization;
using System.IO;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.Storage;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string LocaleArgumentPrefix = "--ui-locale=";
    private const string NativeLibraryFileName = "vrrecorder_native.dll";
    private readonly object _steamVrInputGate = new();
    private readonly DesktopTrayUiController _trayUi = new();
    private readonly DesktopRecordingCommandHost _recordingHost;
    private readonly RecordingInputDispatcher _recordingInputs;
    private readonly DesktopRecordingSettingsController _recordingSettings;
    private readonly AuthenticatedLegalBundleVerifier _legalVerifier;
    private readonly DesktopLegalController _legalController;
    private readonly JsonFileRecordingRightsAcknowledgementStore
        _recordingRightsStore;
    private readonly RecordingRightsGate _recordingRightsGate;
    private readonly CancellationTokenSource _steamVrInputLifetime = new();
    private Task? _steamVrInputTask;
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
        _legalVerifier = new AuthenticatedLegalBundleVerifier(
            new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
                typeof(App).Assembly));
        _recordingSettings = new DesktopRecordingSettingsController(
            settingsStore,
            new RecordingOutputPathResolver(
                new WindowsDownloadsOutputPathProvider()),
            new AuthenticatedLegalBundleOutputMirror(
                AppContext.BaseDirectory,
                ProductVersion(),
                _legalVerifier));
        _recordingHost = new DesktopRecordingCommandHost(
            new ProductionDesktopRecordingRuntimeFactory(settingsStore));
        _recordingRightsStore =
            new JsonFileRecordingRightsAcknowledgementStore(
                RecordingRightsPath(settingsPath));
        _recordingRightsGate = new RecordingRightsGate(
            _recordingRightsStore,
            SystemWallClock.Instance);
        _legalController = new DesktopLegalController(
            new AuthenticatedLegalCatalogReader(
                AppContext.BaseDirectory,
                _legalVerifier,
                LegalBundleVerificationScope.InstallRoot),
            new AuthenticatedLegalBundleFolderOpener(
                AppContext.BaseDirectory,
                AppContext.BaseDirectory,
                _legalVerifier,
                new WindowsLegalFolderShell(),
                LegalBundleVerificationScope.InstallRoot),
            _recordingHost);
        _recordingInputs = new RecordingInputDispatcher(
            new RecordingUiCommandDispatcher(
                (_, cancellationToken) =>
                    _recordingHost.ToggleAsync(cancellationToken)));
    }

    internal static RecordingInputDispatcher RecordingInputs =>
        ((App)Current)._recordingInputs;

    internal static DesktopLegalController LegalController =>
        ((App)Current)._legalController;

    internal static DesktopRecordingSettingsController RecordingSettings =>
        ((App)Current)._recordingSettings;

    internal static IRecorderStatusSource RecordingStatuses =>
        ((App)Current)._recordingHost;

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
        SelectLocalizedResources(
            localeName is null
                ? CultureInfo.CurrentUICulture.Name
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
                _recordingRightsStore.Dispose();
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

            var nativeLibraryPath = Path.Combine(
                AppContext.BaseDirectory,
                NativeLibraryFileName);
            _steamVrInputTask = Task.Run(() =>
                RunSteamVrInputAsync(nativeLibraryPath));
        }
    }

    private async Task RunSteamVrInputAsync(string nativeLibraryPath)
    {
        try
        {
            var runtime = new NativeSteamVrInputRuntime(
                nativeLibraryPath,
                AppContext.BaseDirectory);
            await new SteamVrRecordingInputAdapter(runtime, _recordingInputs)
                .RunAsync(_steamVrInputLifetime.Token)
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

    private void ReplaceMergedResource(string filePrefix, string resourcePath)
    {
        var resource = Resources.MergedDictionaries.Single(dictionary =>
            Path.GetFileName(dictionary.Source?.OriginalString ?? string.Empty)
                .StartsWith(filePrefix, StringComparison.Ordinal));
        resource.Source = new Uri(resourcePath, UriKind.Relative);
    }

}
