using System.Globalization;
using VRRecorder.DesignSystem;

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
