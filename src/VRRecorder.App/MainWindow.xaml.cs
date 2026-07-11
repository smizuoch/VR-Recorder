using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.App;

public partial class MainWindow : Window
{
    private readonly RecordingInputDispatcher _recordingInputs;
    private readonly DesktopRecordingUiController _recordingUi = new();
    private readonly IDisposable _recordingNotificationSubscription;
    private readonly IDisposable _recordingStatusSubscription;
    private bool _startupApplied;
    private bool _recordingCommandsAuthorized;
    private DiagnosticsWindow? _diagnosticsWindow;
    private LegalWindow? _legalWindow;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
        : this(
            App.RecordingInputs,
            App.RecordingStatuses,
            App.RecordingNotifications)
    {
    }

    internal MainWindow(
        RecordingInputDispatcher recordingInputs,
        IRecorderStatusSource recordingStatuses,
        DesktopRecordingNotificationHub recordingNotifications)
    {
        ArgumentNullException.ThrowIfNull(recordingInputs);
        ArgumentNullException.ThrowIfNull(recordingStatuses);
        ArgumentNullException.ThrowIfNull(recordingNotifications);
        _recordingInputs = recordingInputs;
        InitializeComponent();
        _recordingStatusSubscription = recordingStatuses.Subscribe(
            OnRecordingStatusChanged);
        _recordingNotificationSubscription = recordingNotifications.Subscribe(
            OnRecordingNotification);
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
        RecordingToggleButton.IsEnabled =
            isReady && _recordingCommandsAuthorized;
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
        if (startup.State == RecorderState.Ready &&
            activation.State == DesktopRecordingHostState.Ready)
        {
            bool rightsAcknowledged;
            try
            {
                rightsAcknowledged =
                    await App.IsRecordingRightsAcknowledgedAsync(
                        CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ApplyRightsPersistenceError();
                return;
            }

            if (!rightsAcknowledged)
            {
                var rightsWindow = new RecordingRightsWindow
                {
                    Owner = this,
                };
                if (rightsWindow.ShowDialog() != true)
                {
                    App.ExitAfterRightsDeclined();
                    return;
                }

                try
                {
                    await App.AcknowledgeRecordingRightsAsync(
                        CancellationToken.None);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    ApplyRightsPersistenceError();
                    return;
                }
            }

            _recordingCommandsAuthorized = true;
        }

        ApplyStartupResult(startup, activation);
    }

    private void ApplyRightsPersistenceError()
    {
        RecordingToggleButton.IsEnabled = false;
        RecordingStatusText.SetResourceReference(
            TextBlock.TextProperty,
            "Rights_Persistence_Error");
        RecordingStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            "Rights_Persistence_Error");
    }

    protected override void OnClosed(EventArgs e)
    {
        _recordingNotificationSubscription.Dispose();
        _recordingStatusSubscription.Dispose();
        base.OnClosed(e);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.IsExitRequested)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnRecordingStatusChanged(RecorderStatusSnapshot status)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyRecordingStatus(status);
            return;
        }

        _ = Dispatcher.InvokeAsync(() => ApplyRecordingStatus(status));
    }

    private void ApplyRecordingStatus(RecorderStatusSnapshot status)
    {
        var update = _recordingUi.Apply(status);
        if (update is null)
        {
            return;
        }

        if (update.State == RecorderState.Arming)
        {
            ClearRecordingNotifications();
        }

        RecordingStatusText.SetResourceReference(
            TextBlock.TextProperty,
            update.StatusTextResourceKey);
        RecordingStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            update.StatusAccessibleDescriptionResourceKey);
        RecordingToggleButton.SetResourceReference(
            ContentControl.ContentProperty,
            update.ActionLabelResourceKey);
        RecordingToggleButton.SetResourceReference(
            AutomationProperties.NameProperty,
            update.ActionAccessibleNameResourceKey);
        RecordingToggleButton.SetResourceReference(
            AutomationProperties.HelpTextProperty,
            update.ActionHelpResourceKey);
        RecordingToggleButton.SetResourceReference(
            FrameworkElement.ToolTipProperty,
            update.ActionHelpResourceKey);
        RecordingToggleButton.IsEnabled =
            _recordingCommandsAuthorized && update.IsActionEnabled;
    }

    private void OnRecordingNotification(
        DesktopRecordingNotification notification)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyRecordingNotification(notification);
            return;
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(() =>
                ApplyRecordingNotification(notification));
        }
    }

    private void ApplyRecordingNotification(
        DesktopRecordingNotification notification)
    {
        switch (notification)
        {
            case DesktopRecordingNotification.Saved saved:
                {
                    var text = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizedString("Recording_Notification_Saved_Format"),
                        saved.Recording.FinalPath);
                    RecordingSavedText.Text = text;
                    AutomationProperties.SetName(RecordingSavedText, text);
                    RecordingSavedText.Visibility = Visibility.Visible;
                    break;
                }
            case DesktopRecordingNotification.CameraWarning:
                {
                    var text = LocalizedString(
                        "Recording_Notification_CameraRestoreWarning");
                    CameraRestoreWarningText.Text = text;
                    AutomationProperties.SetName(
                        CameraRestoreWarningText,
                        text);
                    CameraRestoreWarningText.Visibility = Visibility.Visible;
                    break;
                }
        }
    }

    private void ClearRecordingNotifications()
    {
        RecordingSavedText.Text = string.Empty;
        AutomationProperties.SetName(RecordingSavedText, string.Empty);
        RecordingSavedText.Visibility = Visibility.Collapsed;
        CameraRestoreWarningText.Text = string.Empty;
        AutomationProperties.SetName(
            CameraRestoreWarningText,
            string.Empty);
        CameraRestoreWarningText.Visibility = Visibility.Collapsed;
    }

    private string LocalizedString(string resourceKey) =>
        TryFindResource(resourceKey) as string ??
        throw new InvalidOperationException(
            $"The localized resource {resourceKey} is missing.");

    private async void OnRecordingToggleClick(
        object sender,
        RoutedEventArgs e) =>
        await DispatchRecordingAsync(UiActivationKind.DesktopClick);

    private void OnAboutLegalClick(object sender, RoutedEventArgs e)
    {
        OpenLegalWindow();
    }

    internal void OpenLegalWindow()
    {
        if (_legalWindow is { IsVisible: true })
        {
            _legalWindow.Activate();
            return;
        }

        var legalWindow = new LegalWindow(App.LegalController)
        {
            Owner = this,
        };
        legalWindow.Closed += (_, _) => _legalWindow = null;
        _legalWindow = legalWindow;
        legalWindow.Show();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        OpenSettingsWindow();

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e) =>
        OpenDiagnosticsWindow();

    internal void OpenDiagnosticsWindow()
    {
        if (_diagnosticsWindow is { IsVisible: true })
        {
            _diagnosticsWindow.Activate();
            return;
        }

        var diagnosticsWindow = new DiagnosticsWindow
        {
            Owner = this,
        };
        diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow = diagnosticsWindow;
        diagnosticsWindow.Show();
    }

    internal void OpenSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsWindow = new SettingsWindow
        {
            Owner = this,
        };
        settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = settingsWindow;
        settingsWindow.Show();
    }

    private async void OnPreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
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
