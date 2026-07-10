using System.Globalization;
using System.IO;
using VRRecorder.Application.Compliance;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.App;

public partial class App : System.Windows.Application
{
    private readonly RecordingInputDispatcher _recordingInputs = new(
        new UnavailableUiCommandDispatcher());

    internal static RecordingInputDispatcher RecordingInputs =>
        ((App)Current)._recordingInputs;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        SelectLocalizedResources(CultureInfo.CurrentUICulture);
        base.OnStartup(e);
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

    private void SelectLocalizedResources(CultureInfo culture)
    {
        var resourcePath = string.Equals(
            culture.TwoLetterISOLanguageName,
            "ja",
            StringComparison.OrdinalIgnoreCase)
            ? "Resources/Strings.ja-JP.xaml"
            : "Resources/Strings.en-US.xaml";
        var strings = Resources.MergedDictionaries.Single(dictionary =>
            dictionary.Source?.OriginalString.EndsWith(
                "Strings.en-US.xaml",
                StringComparison.Ordinal) == true);
        strings.Source = new Uri(resourcePath, UriKind.Relative);
    }

    private sealed class UnavailableUiCommandDispatcher : IUiCommandDispatcher
    {
        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException(new InvalidOperationException(
                "The recording command handler is not configured."));
        }
    }
}
