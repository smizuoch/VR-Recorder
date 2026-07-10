using System.Globalization;
using System.IO;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string LocaleArgumentPrefix = "--ui-locale=";
    private readonly DesktopRecordingCommandHost _recordingHost = new(
        new ProductionDesktopRecordingRuntimeFactory());
    private readonly RecordingInputDispatcher _recordingInputs;

    public App()
    {
        _recordingInputs = new RecordingInputDispatcher(
            new RecordingUiCommandDispatcher(
                (_, cancellationToken) =>
                    _recordingHost.ToggleAsync(cancellationToken)));
    }

    internal static RecordingInputDispatcher RecordingInputs =>
        ((App)Current)._recordingInputs;

    internal static Task<DesktopRecordingHostActivation>
        ActivateRecordingHostAsync(
            RecorderStartupResult startup,
            CancellationToken cancellationToken) =>
        ((App)Current)._recordingHost.ActivateAsync(
            startup,
            cancellationToken);

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
        _recordingHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    internal static async Task<RecorderStartupResult> VerifyStartupAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var anchorSource =
                new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
                    typeof(App).Assembly);
            var verifier = new AuthenticatedLegalBundleVerifier(anchorSource);
            var gateway = new RuntimeLegalBundleVerificationGateway(
                AppContext.BaseDirectory,
                verifier);
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
