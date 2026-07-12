using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VRRecorder.App;

public partial class SettingsWindow : Window, IDisposable
{
    private readonly DesktopRecordingSettingsController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private DesktopRecordingSettingsDraft? _draft;
    private string? _selectedOutputFolder;
    private bool _loadStarted;
    private bool _saving;
    private int _disposeStarted;

    public SettingsWindow()
        : this(App.RecordingSettings)
    {
    }

    internal SettingsWindow(DesktopRecordingSettingsController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
        PopulateChoices();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        try
        {
            _draft = await _controller.LoadAsync(_lifetime.Token);
            var endpointOptions = await _controller
                .LoadAudioEndpointOptionsAsync(_draft, _lifetime.Token);
            SelectOutputFolder(_draft.OutputFolder);
            BrowseOutputFolderButton.IsEnabled = true;
            UseDownloadsButton.IsEnabled = true;
            SelectValue(SelfTimerComboBox, _draft.SelfTimerSeconds);
            SelectValue(AutoStopComboBox, _draft.AutoStopSeconds);
            SelectFrameRate(_draft.FrameRate);
            SelectValue(
                ResolutionPolicyComboBox,
                _draft.ResolutionChangePolicy);
            SelectValue(EncoderComboBox, _draft.Encoder);
            SelectValue(QualityPresetComboBox, _draft.QualityPreset);
            SelectValue(AudioRoutingComboBox, _draft.AudioRouting);
            SelectValue(UiLocaleComboBox, _draft.UiLocale);
            SelectValue(VrHandComboBox, _draft.VrHand);
            SelectValue(
                OverlayPlacementComboBox,
                _draft.OverlayPlacement);
            SelectValue(
                OscAutoDiscoverComboBox,
                _draft.OscAutoDiscover);
            OscFallbackHostTextBox.Text = _draft.OscFallbackHost;
            OscFallbackSendPortTextBox.Text = _draft.OscFallbackSendPort
                .ToString(CultureInfo.InvariantCulture);
            OscFallbackReceivePortTextBox.Text = _draft.OscFallbackReceivePort
                .ToString(CultureInfo.InvariantCulture);
            BindEndpointOptions(
                DesktopEndpointComboBox,
                endpointOptions.Desktop,
                _draft.DesktopEndpointId);
            BindEndpointOptions(
                MicrophoneEndpointComboBox,
                endpointOptions.Microphone,
                _draft.MicrophoneEndpointId);
            DesktopGainSlider.Value = _draft.DesktopGainDb;
            MicrophoneGainSlider.Value = _draft.MicrophoneGainDb;
            UpdateGainLabels();
            UpdateSaveAvailability();
        }
        catch (OperationCanceledException) when (
            _lifetime.IsCancellationRequested)
        {
            // Closing the settings window cancels its pending load.
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            ApplyError("Settings_Load_Error");
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _saving || !HasEverySelection())
        {
            return;
        }

        _saving = true;
        UpdateSaveAvailability();
        try
        {
            var updated = _draft with
            {
                OutputFolder = _selectedOutputFolder!,
                SelfTimerSeconds = SelectedValue<int>(SelfTimerComboBox),
                AutoStopSeconds = SelectedNullableInt(AutoStopComboBox),
                FrameRate = SelectedValue<int>(FrameRateComboBox),
                ResolutionChangePolicy =
                    SelectedValue<ResolutionChangePolicy>(
                        ResolutionPolicyComboBox),
                Encoder = SelectedValue<EncoderPreference>(EncoderComboBox),
                QualityPreset = SelectedValue<VideoQualityPreset>(
                    QualityPresetComboBox),
                AudioRouting = SelectedValue<AudioRouting>(
                    AudioRoutingComboBox),
                DesktopEndpointId = SelectedEndpoint(
                    DesktopEndpointComboBox),
                MicrophoneEndpointId = SelectedEndpoint(
                    MicrophoneEndpointComboBox),
                UiLocale = SelectedValue<UiLocale>(UiLocaleComboBox),
                VrHand = SelectedValue<VrHand>(VrHandComboBox),
                OverlayPlacement =
                    SelectedValue<OverlayPlacementMode>(
                        OverlayPlacementComboBox),
                OscAutoDiscover = SelectedValue<bool>(
                    OscAutoDiscoverComboBox),
                OscFallbackHost = OscFallbackHostTextBox.Text,
                OscFallbackSendPort = ParsePort(OscFallbackSendPortTextBox),
                OscFallbackReceivePort = ParsePort(
                    OscFallbackReceivePortTextBox),
                DesktopGainDb = DesktopGainSlider.Value,
                MicrophoneGainDb = MicrophoneGainSlider.Value,
            };
            await _controller.SaveAsync(_draft, updated, _lifetime.Token);
            _draft = updated;
            Close();
        }
        catch (OperationCanceledException) when (
            _lifetime.IsCancellationRequested)
        {
            // Closing the settings window cancels its pending save.
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            ApplyError("Settings_Save_Error");
        }
        finally
        {
            _saving = false;
            UpdateSaveAvailability();
        }
    }

    private void OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e) => UpdateSaveAvailability();

    private void OnGainChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (DesktopGainValueText is null ||
            MicrophoneGainValueText is null)
        {
            return;
        }

        UpdateGainLabels();
        UpdateSaveAvailability();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnBrowseOutputFolder(object sender, RoutedEventArgs e)
    {
        if (_selectedOutputFolder is null)
        {
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Resource("Settings_Output_Browse_DialogTitle"),
            SelectedPath = _controller
                .ResolveOutputPath(_selectedOutputFolder)
                .FullPath,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        SelectOutputFolder(dialog.SelectedPath);
        UpdateSaveAvailability();
    }

    private void OnUseDownloads(object sender, RoutedEventArgs e)
    {
        SelectOutputFolder(
            RecordingOutputPathResolver.DownloadsKnownFolderToken);
        UpdateSaveAvailability();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private void PopulateChoices()
    {
        SelfTimerComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedSelfTimerSeconds
                .Select(value => new SettingOption(
                    value,
                    value == 0
                        ? Resource("Settings_Choice_Off")
                        : Format("Settings_Choice_Seconds_Format", value)))
                .ToList();
        AutoStopComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedAutoStopSeconds
                .Select(value => new SettingOption(
                    value,
                    value is null
                        ? Resource("Settings_Choice_Infinite")
                        : Format(
                            "Settings_Choice_Seconds_Format",
                            value.Value)))
                .ToList();
        FrameRateComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedFrameRates
                .Select(value => new SettingOption(
                    value,
                    Format("Settings_Choice_Fps_Format", value)))
                .ToList();
        ResolutionPolicyComboBox.ItemsSource =
            DesktopRecordingSettingsController
                .SupportedResolutionChangePolicies
                .Select(value => new SettingOption(
                    value,
                    Resource(value switch
                    {
                        ResolutionChangePolicy.SingleFileFit =>
                            "Settings_Resolution_SingleFileFit",
                        ResolutionChangePolicy.ExactFollowSegments =>
                            "Settings_Resolution_ExactFollowSegments",
                        _ => throw new InvalidDataException(
                            "Unsupported resolution policy choice."),
                    })))
                .ToList();
        EncoderComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedEncoders
                .Select(value => new SettingOption(
                    value,
                    Resource(value switch
                    {
                        EncoderPreference.Auto => "Settings_Encoder_Auto",
                        EncoderPreference.Nvenc => "Settings_Encoder_Nvenc",
                        EncoderPreference.Amf => "Settings_Encoder_Amf",
                        EncoderPreference.Qsv => "Settings_Encoder_Qsv",
                        EncoderPreference.MediaFoundationSoftware =>
                            "Settings_Encoder_MediaFoundationSoftware",
                        _ => throw new InvalidDataException(
                            "Unsupported encoder choice."),
                    })))
                .ToList();
        QualityPresetComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedQualityPresets
                .Select(value => new SettingOption(
                    value,
                    Resource(value switch
                    {
                        VideoQualityPreset.Standard =>
                            "Settings_Quality_Standard",
                        VideoQualityPreset.High => "Settings_Quality_High",
                        _ => throw new InvalidDataException(
                            "Unsupported quality choice."),
                    })))
                .ToList();
        AudioRoutingComboBox.ItemsSource =
            DesktopRecordingSettingsController.SupportedAudioRoutings
                .Select(value => new SettingOption(
                    value,
                    Resource(value switch
                    {
                        AudioRouting.Mixed => "Settings_Audio_Mixed",
                        AudioRouting.DesktopOnly =>
                            "Settings_Audio_DesktopOnly",
                        AudioRouting.MicOnly => "Settings_Audio_MicOnly",
                        AudioRouting.Muted => "Settings_Audio_Muted",
                        _ => throw new InvalidDataException(
                            "Unsupported audio routing choice."),
                    })))
                .ToList();
        UiLocaleComboBox.ItemsSource = Enum.GetValues<UiLocale>()
            .Select(value => new SettingOption(
                value,
                Resource(value switch
                {
                    UiLocale.System => "Settings_Language_System",
                    UiLocale.English => "Settings_Language_English",
                    UiLocale.Japanese => "Settings_Language_Japanese",
                    _ => throw new InvalidDataException(
                        "Unsupported UI locale choice."),
                })))
            .ToList();
        VrHandComboBox.ItemsSource = Enum.GetValues<VrHand>()
            .Select(value => new SettingOption(
                value,
                Resource(value == VrHand.Left
                    ? "Settings_VrHand_Left"
                    : "Settings_VrHand_Right")))
            .ToList();
        OverlayPlacementComboBox.ItemsSource =
            Enum.GetValues<OverlayPlacementMode>()
                .Select(value => new SettingOption(
                    value,
                    Resource(value == OverlayPlacementMode.WristDock
                        ? "Settings_OverlayPlacement_WristDock"
                        : "Settings_OverlayPlacement_WorldPin")))
                .ToList();
        OscAutoDiscoverComboBox.ItemsSource = new[]
        {
            new SettingOption(true, Resource("Settings_Osc_Auto")),
            new SettingOption(false, Resource("Settings_Osc_Fallback")),
        };
    }

    private static void BindEndpointOptions(
        WpfComboBox comboBox,
        IReadOnlyList<AudioEndpointOption> endpoints,
        string endpointId)
    {
        comboBox.ItemsSource = endpoints;
        comboBox.SelectedItem = endpoints.Single(endpoint =>
            string.Equals(endpoint.Id, endpointId, StringComparison.Ordinal));
    }

    private static string SelectedEndpoint(WpfComboBox comboBox)
    {
        var endpointId = comboBox.SelectedItem is AudioEndpointOption selected &&
                         string.Equals(
                             comboBox.Text,
                             selected.DisplayName,
                             StringComparison.Ordinal)
            ? selected.Id
            : comboBox.Text;
        return string.IsNullOrWhiteSpace(endpointId)
            ? throw new InvalidDataException(
                "An audio endpoint must be selected.")
            : endpointId;
    }

    private void SelectFrameRate(int value)
    {
        var options = (List<SettingOption>)FrameRateComboBox.ItemsSource;
        if (options.All(option => !Equals(option.Value, value)))
        {
            options.Add(new SettingOption(
                value,
                Format("Settings_Choice_Fps_Format", value)));
            FrameRateComboBox.Items.Refresh();
        }

        SelectValue(FrameRateComboBox, value);
    }

    private void SelectOutputFolder(string configuredPath)
    {
        var resolved = _controller.ResolveOutputPath(configuredPath);
        _selectedOutputFolder = configuredPath;
        OutputFolderTextBox.Text = resolved.FullPath;
    }

    private void UpdateGainLabels()
    {
        DesktopGainValueText.Text = Format(
            "Settings_Audio_Gain_Format",
            DesktopGainSlider.Value);
        MicrophoneGainValueText.Text = Format(
            "Settings_Audio_Gain_Format",
            MicrophoneGainSlider.Value);
    }

    private static void SelectValue(WpfComboBox comboBox, object? value) =>
        comboBox.SelectedItem = comboBox.Items
            .Cast<SettingOption>()
            .Single(option => Equals(option.Value, value));

    private static T SelectedValue<T>(WpfComboBox comboBox) =>
        comboBox.SelectedItem is SettingOption { Value: T value }
            ? value
            : throw new InvalidDataException(
                "A required settings choice is not selected.");

    private static int? SelectedNullableInt(WpfComboBox comboBox) =>
        comboBox.SelectedItem is SettingOption option
            ? option.Value switch
            {
                null => null,
                int value => value,
                _ => throw new InvalidDataException(
                    "The auto-stop choice is invalid."),
            }
            : throw new InvalidDataException(
                "The auto-stop choice is not selected.");

    private static int ParsePort(WpfTextBox textBox) =>
        int.TryParse(
            textBox.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port)
            ? port
            : throw new InvalidDataException("The OSC port is invalid.");

    private void OnSettingsTextChanged(
        object sender,
        TextChangedEventArgs e) => UpdateSaveAvailability();

    private bool HasEverySelection() =>
        _selectedOutputFolder is not null &&
        SelfTimerComboBox.SelectedItem is SettingOption &&
        AutoStopComboBox.SelectedItem is SettingOption &&
        FrameRateComboBox.SelectedItem is SettingOption &&
        ResolutionPolicyComboBox.SelectedItem is SettingOption &&
        EncoderComboBox.SelectedItem is SettingOption &&
        QualityPresetComboBox.SelectedItem is SettingOption &&
        AudioRoutingComboBox.SelectedItem is SettingOption &&
        UiLocaleComboBox.SelectedItem is SettingOption &&
        VrHandComboBox.SelectedItem is SettingOption &&
        OverlayPlacementComboBox.SelectedItem is SettingOption &&
        OscAutoDiscoverComboBox.SelectedItem is SettingOption &&
        !string.IsNullOrWhiteSpace(OscFallbackHostTextBox.Text) &&
        !string.IsNullOrWhiteSpace(OscFallbackSendPortTextBox.Text) &&
        !string.IsNullOrWhiteSpace(OscFallbackReceivePortTextBox.Text) &&
        !string.IsNullOrWhiteSpace(DesktopEndpointComboBox.Text) &&
        !string.IsNullOrWhiteSpace(MicrophoneEndpointComboBox.Text);

    private void UpdateSaveAvailability() =>
        SaveSettingsButton.IsEnabled =
            _draft is not null && !_saving && HasEverySelection();

    private void ApplyError(string resourceKey)
    {
        SaveSettingsButton.IsEnabled = false;
        SettingsStatusText.SetResourceReference(
            TextBlock.TextProperty,
            resourceKey);
        SettingsStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            resourceKey);
    }

    private string Resource(string key) =>
        FindResource(key) as string ?? throw new InvalidOperationException(
            $"The settings resource {key} is missing.");

    private string Format(string key, object value) => string.Format(
        CultureInfo.CurrentCulture,
        Resource(key),
        value);

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException;

    private sealed record SettingOption(object? Value, string Label);
}
