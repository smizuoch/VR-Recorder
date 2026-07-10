using System.Globalization;
using System.IO;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string LocaleArgumentPrefix = "--ui-locale=";
    private const string NativeLibraryFileName = "vrrecorder_native.dll";
    private readonly object _steamVrInputGate = new();
    private readonly DesktopRecordingCommandHost _recordingHost = new(
        new ProductionDesktopRecordingRuntimeFactory());
    private readonly RecordingInputDispatcher _recordingInputs;
    private readonly AuthenticatedLegalBundleVerifier _legalVerifier;
    private readonly DesktopLegalController _legalController;
    private readonly CancellationTokenSource _steamVrInputLifetime = new();
    private Task? _steamVrInputTask;
    private int _disposeStarted;

    public App()
    {
        _legalVerifier = new AuthenticatedLegalBundleVerifier(
            new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
                typeof(App).Assembly));
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

    internal static IRecorderStatusSource RecordingStatuses =>
        ((App)Current)._recordingHost;

    internal static async Task<DesktopRecordingHostActivation>
        ActivateRecordingHostAsync(
            RecorderStartupResult startup,
            CancellationToken cancellationToken)
    {
        var app = (App)Current;
        var activation = await app._recordingHost
            .ActivateAsync(startup, cancellationToken)
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
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
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
            _recordingHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

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
