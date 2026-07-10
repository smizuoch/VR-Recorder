using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VRRecorder.Application.Compliance;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.App;

public partial class MainWindow : Window
{
    private readonly RecordingInputDispatcher _recordingInputs;
    private bool _startupApplied;

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

    internal void ApplyStartupResult(RecorderStartupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var isReady = result.State == RecorderState.Ready;
        if (!isReady && result.State != RecorderState.ComplianceFault)
        {
            throw new InvalidOperationException(
                $"Startup cannot complete in recorder state {result.State}.");
        }

        RecordingToggleButton.IsEnabled = isReady;
        RecordingStatusText.SetResourceReference(
            TextBlock.TextProperty,
            isReady
                ? "Recording_State_Ready"
                : "Recording_State_ComplianceFault");
        RecordingStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            isReady
                ? "Status_Ready_AccessibleDescription"
                : "Status_ComplianceFault_AccessibleDescription");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupApplied)
        {
            return;
        }

        _startupApplied = true;
        ApplyStartupResult(await App.VerifyStartupAsync(CancellationToken.None));
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
