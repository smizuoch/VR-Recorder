using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VRRecorder.DesignSystem;

namespace VRRecorder.App;

public partial class MainWindow : Window
{
    private readonly RecordingInputDispatcher _recordingInputs;

    public MainWindow()
        : this(App.RecordingInputs)
    {
    }

    internal MainWindow(RecordingInputDispatcher recordingInputs)
    {
        ArgumentNullException.ThrowIfNull(recordingInputs);
        _recordingInputs = recordingInputs;
        InitializeComponent();
    }

    private async void OnRecordingToggleClick(
        object sender,
        RoutedEventArgs e) =>
        await DispatchRecordingAsync(UiActivationKind.DesktopClick);

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.R ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        await DispatchRecordingAsync(UiActivationKind.DesktopKeyboard);
    }

    private async Task DispatchRecordingAsync(UiActivationKind activationKind)
    {
        try
        {
            await _recordingInputs.DispatchAsync(
                activationKind,
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            RecordingStatusText.SetResourceReference(
                TextBlock.TextProperty,
                "Status_CommandUnavailable");
            RecordingStatusText.SetResourceReference(
                AutomationProperties.NameProperty,
                "Status_CommandUnavailable");
        }
    }
}
