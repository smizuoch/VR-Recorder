using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace VRRecorder.App;

public partial class SettingsWindow : Window, IDisposable
{
    private readonly DesktopRecordingSettingsController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private DesktopRecordingSettingsDraft? _draft;
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
            SelectValue(SelfTimerComboBox, _draft.SelfTimerSeconds);
            SelectValue(AutoStopComboBox, _draft.AutoStopSeconds);
            SelectFrameRate(_draft.FrameRate);
            SelectValue(
                ResolutionPolicyComboBox,
                _draft.ResolutionChangePolicy);
            SelectValue(EncoderComboBox, _draft.Encoder);
            SelectValue(QualityPresetComboBox, _draft.QualityPreset);
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
                SelfTimerSeconds = SelectedValue<int>(SelfTimerComboBox),
                AutoStopSeconds = SelectedNullableInt(AutoStopComboBox),
                FrameRate = SelectedValue<int>(FrameRateComboBox),
                ResolutionChangePolicy =
                    SelectedValue<ResolutionChangePolicy>(
                        ResolutionPolicyComboBox),
                Encoder = SelectedValue<EncoderPreference>(EncoderComboBox),
                QualityPreset = SelectedValue<VideoQualityPreset>(
                    QualityPresetComboBox),
            };
            await _controller.SaveAsync(updated, _lifetime.Token);
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

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

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

    private bool HasEverySelection() =>
        SelfTimerComboBox.SelectedItem is SettingOption &&
        AutoStopComboBox.SelectedItem is SettingOption &&
        FrameRateComboBox.SelectedItem is SettingOption &&
        ResolutionPolicyComboBox.SelectedItem is SettingOption &&
        EncoderComboBox.SelectedItem is SettingOption &&
        QualityPresetComboBox.SelectedItem is SettingOption;

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
