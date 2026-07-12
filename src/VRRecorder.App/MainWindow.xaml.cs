using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly IUiCommandDispatcher _uiCommands;
    private readonly DesktopRecordingUiController _recordingUi = new();
    private readonly DesktopRecordingAudioUiController _recordingAudioUi =
        new();
    private readonly DesktopAudioAvailabilityUiController _audioAvailabilityUi =
        new();
    private readonly IDisposable _recordingNotificationSubscription;
    private readonly IDisposable _recordingStatusSubscription;
    private bool _startupApplied;
    private bool _recordingCommandsAuthorized;
    private bool _audioCommandPending;
    private DiagnosticsWindow? _diagnosticsWindow;
    private LegalWindow? _legalWindow;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
        : this(
            App.RecordingInputs,
            App.UiCommands,
            App.RecordingStatuses,
            App.RecordingNotifications)
    {
    }

    internal MainWindow(
        RecordingInputDispatcher recordingInputs,
        IUiCommandDispatcher uiCommands,
        IRecorderStatusSource recordingStatuses,
        DesktopRecordingNotificationHub recordingNotifications)
    {
        ArgumentNullException.ThrowIfNull(recordingInputs);
        ArgumentNullException.ThrowIfNull(uiCommands);
        ArgumentNullException.ThrowIfNull(recordingStatuses);
        ArgumentNullException.ThrowIfNull(recordingNotifications);
        _recordingInputs = recordingInputs;
        _uiCommands = uiCommands;
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
            try
            {
                var setup = await App.FirstRunSetupUi.LoadAsync(
                    CancellationToken.None);
                if (setup.RequiresSetup)
                {
                    var setupWindow = new FirstRunSetupWindow
                    {
                        Owner = this,
                    };
                    setupWindow.ShowDialog();
                    setup = await App.FirstRunSetupUi.LoadAsync(
                        CancellationToken.None);
                    if (setup.RequiresSetup)
                    {
                        ApplySetupRequiredState(startup, activation);
                        return;
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ApplySetupPersistenceError();
                return;
            }

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

    private void ApplySetupRequiredState(
        RecorderStartupResult startup,
        DesktopRecordingHostActivation activation)
    {
        ApplyStartupResult(startup, activation);
        RecordingToggleButton.IsEnabled = false;
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

    private void ApplySetupPersistenceError()
    {
        RecordingToggleButton.IsEnabled = false;
        RecordingStatusText.SetResourceReference(
            TextBlock.TextProperty,
            "Setup_Persistence_Error");
        RecordingStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            "Setup_Persistence_Error");
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

        var audioAvailability = _audioAvailabilityUi.Apply(status);
        if (audioAvailability is not null)
        {
            ApplyAudioAvailability(audioAvailability);
        }

        var recordingAudio = _recordingAudioUi.Apply(status);
        if (recordingAudio is not null)
        {
            ApplyRecordingAudio(recordingAudio);
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

    private void ApplyRecordingAudio(
        DesktopRecordingAudioUiSnapshot snapshot)
    {
        AudioControlsPanel.Visibility = snapshot.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        MicrophoneToggleButton.SetResourceReference(
            ContentControl.ContentProperty,
            snapshot.MicrophoneLabelResourceKey);
        MicrophoneToggleButton.SetResourceReference(
            AutomationProperties.NameProperty,
            snapshot.MicrophoneAccessibleNameResourceKey);
        MicrophoneToggleButton.SetResourceReference(
            AutomationProperties.HelpTextProperty,
            snapshot.MicrophoneHelpResourceKey);
        MicrophoneToggleButton.SetResourceReference(
            FrameworkElement.ToolTipProperty,
            snapshot.MicrophoneHelpResourceKey);
        MicrophoneToggleButton.SetCurrentValue(
            ToggleButton.IsCheckedProperty,
            snapshot.IsMicrophoneSelected);
        MuteAllToggleButton.SetResourceReference(
            ContentControl.ContentProperty,
            snapshot.MuteAllLabelResourceKey);
        MuteAllToggleButton.SetResourceReference(
            AutomationProperties.NameProperty,
            snapshot.MuteAllAccessibleNameResourceKey);
        MuteAllToggleButton.SetResourceReference(
            AutomationProperties.HelpTextProperty,
            snapshot.MuteAllHelpResourceKey);
        MuteAllToggleButton.SetResourceReference(
            FrameworkElement.ToolTipProperty,
            snapshot.MuteAllHelpResourceKey);
        MuteAllToggleButton.SetCurrentValue(
            ToggleButton.IsCheckedProperty,
            snapshot.IsMuteAllSelected);
        var isEnabled = _recordingCommandsAuthorized &&
                        snapshot.IsEnabled &&
                        !_audioCommandPending;
        MicrophoneToggleButton.IsEnabled = isEnabled;
        MuteAllToggleButton.IsEnabled = isEnabled;
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
            case DesktopRecordingNotification.AudioWarning audioWarning:
                {
                    var update = _audioAvailabilityUi.Apply(audioWarning);
                    if (update is not null)
                    {
                        ApplyAudioAvailability(update);
                    }

                    break;
                }
            case DesktopRecordingNotification.AudioRecovered audioRecovered:
                {
                    var update = _audioAvailabilityUi.Apply(audioRecovered);
                    if (update is not null)
                    {
                        ApplyAudioAvailability(update);
                    }

                    break;
                }
        }
    }

    private void ApplyAudioAvailability(
        DesktopAudioAvailabilityUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsVisible)
        {
            ClearAudioAvailability();
            return;
        }

        var displayText = LocalizedString(snapshot.DisplayResourceKey!);
        AudioDeviceStatusText.Text = displayText;
        AutomationProperties.SetName(AudioDeviceStatusText, displayText);
        AudioDeviceStatusText.Visibility = Visibility.Visible;
        if (snapshot.AnnouncementResourceKey is null)
        {
            return;
        }

        var liveSetting = snapshot.AnnouncementUrgency switch
        {
            DesktopAnnouncementUrgency.Assertive =>
                AutomationLiveSetting.Assertive,
            DesktopAnnouncementUrgency.Polite => AutomationLiveSetting.Polite,
            _ => throw new InvalidOperationException(
                "A visible audio announcement must declare its urgency."),
        };
        AutomationProperties.SetLiveSetting(
            AudioDeviceAnnouncementText,
            liveSetting);
        var announcementText = LocalizedString(
            snapshot.AnnouncementResourceKey);
        AudioDeviceAnnouncementText.Text = announcementText;
        AutomationProperties.SetName(
            AudioDeviceAnnouncementText,
            announcementText);
        var peer = UIElementAutomationPeer.FromElement(
                       AudioDeviceAnnouncementText) ??
                   UIElementAutomationPeer.CreatePeerForElement(
                       AudioDeviceAnnouncementText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

    private void ClearAudioAvailability()
    {
        AudioDeviceStatusText.Text = string.Empty;
        AutomationProperties.SetName(AudioDeviceStatusText, string.Empty);
        AudioDeviceStatusText.Visibility = Visibility.Collapsed;
        AudioDeviceAnnouncementText.Text = string.Empty;
        AutomationProperties.SetName(
            AudioDeviceAnnouncementText,
            string.Empty);
        AutomationProperties.SetLiveSetting(
            AudioDeviceAnnouncementText,
            AutomationLiveSetting.Off);
    }

    private string LocalizedString(string resourceKey) =>
        TryFindResource(resourceKey) as string ??
        throw new InvalidOperationException(
            $"The localized resource {resourceKey} is missing.");

    private async void OnRecordingToggleClick(
        object sender,
        RoutedEventArgs e) =>
        await DispatchRecordingAsync(UiActivationKind.DesktopClick);

    private async void OnMicrophoneToggleClick(
        object sender,
        RoutedEventArgs e) =>
        await DispatchAudioAsync(UiCommandId.ToggleMicrophone);

    private async void OnMuteAllToggleClick(
        object sender,
        RoutedEventArgs e) =>
        await DispatchAudioAsync(UiCommandId.ToggleMuteAll);

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

    private async Task DispatchAudioAsync(UiCommandId command)
    {
        if (_recordingAudioUi.Current is { } authoritative)
        {
            ApplyRecordingAudio(authoritative);
        }

        if (_audioCommandPending)
        {
            return;
        }

        _audioCommandPending = true;
        if (_recordingAudioUi.Current is { } pending)
        {
            ApplyRecordingAudio(pending);
        }

        try
        {
            await _uiCommands.DispatchAsync(
                command,
                UiActivationKind.DesktopClick,
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            if (_recordingAudioUi.Current is { } current)
            {
                ApplyRecordingAudio(current);
            }

            RecordingStatusText.SetResourceReference(
                TextBlock.TextProperty,
                "Status_CommandUnavailable");
            RecordingStatusText.SetResourceReference(
                AutomationProperties.NameProperty,
                "Status_CommandUnavailable");
        }
        finally
        {
            _audioCommandPending = false;
            if (_recordingAudioUi.Current is { } current)
            {
                ApplyRecordingAudio(current);
            }
        }
    }
}
