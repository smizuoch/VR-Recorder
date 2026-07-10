using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
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

    internal void ApplyStartupResult(
        RecorderStartupResult result,
        DesktopRecordingHostActivation activation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(activation);
        if (result.State is not (
                RecorderState.Ready or RecorderState.ComplianceFault))
        {
            throw new InvalidOperationException(
                $"Startup cannot complete in recorder state {result.State}.");
        }

        var expectedHostState = result.State == RecorderState.ComplianceFault
            ? DesktopRecordingHostState.ComplianceFault
            : activation.State;
        if (activation.State != expectedHostState ||
            (result.State == RecorderState.Ready &&
             activation.State is not (
                 DesktopRecordingHostState.Ready or
                 DesktopRecordingHostState.InitializationFailed)))
        {
            throw new InvalidOperationException(
                $"Recorder state {result.State} cannot complete with " +
                $"desktop host state {activation.State}.");
        }

        var isReady = activation.State == DesktopRecordingHostState.Ready;
        var (statusText, accessibleStatus) = activation.State switch
        {
            DesktopRecordingHostState.Ready => (
                "Recording_State_Ready",
                "Status_Ready_AccessibleDescription"),
            DesktopRecordingHostState.ComplianceFault => (
                "Recording_State_ComplianceFault",
                "Status_ComplianceFault_AccessibleDescription"),
            DesktopRecordingHostState.InitializationFailed => (
                "Recording_State_InitializationFailed",
                "Status_InitializationFailed_AccessibleDescription"),
            _ => throw new InvalidOperationException(
                $"Startup cannot complete with desktop host state " +
                $"{activation.State}."),
        };
        RecordingToggleButton.IsEnabled = isReady;
        RecordingStatusText.SetResourceReference(
            TextBlock.TextProperty,
            statusText);
        RecordingStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            accessibleStatus);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupApplied)
        {
            return;
        }

        _startupApplied = true;
        var startup = await App.VerifyStartupAsync(CancellationToken.None);
        var activation = await App.ActivateRecordingHostAsync(
            startup,
            CancellationToken.None);
        ApplyStartupResult(startup, activation);
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
