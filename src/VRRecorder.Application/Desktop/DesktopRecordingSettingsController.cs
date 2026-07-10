using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingSettingsController
{
    private static readonly IReadOnlyList<int> SelfTimerChoices =
        Array.AsReadOnly([0, 3, 5, 10]);
    private static readonly IReadOnlyList<int?> AutoStopChoices =
        Array.AsReadOnly<int?>([null, 3, 5, 10, 30, 60]);
    private static readonly IReadOnlyList<int> FrameRateChoices =
        Array.AsReadOnly([30, 60, 90, 120]);
    private static readonly IReadOnlyList<ResolutionChangePolicy>
        ResolutionPolicyChoices = Array.AsReadOnly(
        [
            ResolutionChangePolicy.SingleFileFit,
            ResolutionChangePolicy.ExactFollowSegments,
        ]);
    private static readonly IReadOnlyList<EncoderPreference> EncoderChoices =
        Array.AsReadOnly(
        [
            EncoderPreference.Auto,
            EncoderPreference.Nvenc,
            EncoderPreference.Amf,
            EncoderPreference.Qsv,
            EncoderPreference.MediaFoundationSoftware,
        ]);
    private static readonly IReadOnlyList<VideoQualityPreset> QualityChoices =
        Array.AsReadOnly(
        [
            VideoQualityPreset.Standard,
            VideoQualityPreset.High,
        ]);

    private readonly ISettingsStore _settings;

    public DesktopRecordingSettingsController(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public static IReadOnlyList<int> SupportedSelfTimerSeconds =>
        SelfTimerChoices;

    public static IReadOnlyList<int?> SupportedAutoStopSeconds =>
        AutoStopChoices;

    public static IReadOnlyList<int> SupportedFrameRates => FrameRateChoices;

    public static IReadOnlyList<ResolutionChangePolicy>
        SupportedResolutionChangePolicies => ResolutionPolicyChoices;

    public static IReadOnlyList<EncoderPreference> SupportedEncoders =>
        EncoderChoices;

    public static IReadOnlyList<VideoQualityPreset> SupportedQualityPresets =>
        QualityChoices;

    public async Task<DesktopRecordingSettingsDraft> LoadAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadValidatedAsync(cancellationToken)
            .ConfigureAwait(false);
        return Project(settings);
    }

    public async Task SaveAsync(
        DesktopRecordingSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateDraft(draft);
        var current = await LoadValidatedAsync(cancellationToken)
            .ConfigureAwait(false);
        var updated = current with
        {
            Recording = current.Recording with
            {
                SelfTimerSeconds = draft.SelfTimerSeconds,
                AutoStopSeconds = draft.AutoStopSeconds,
                ResolutionChangePolicy = draft.ResolutionChangePolicy,
            },
            Video = current.Video with
            {
                FrameRate = draft.FrameRate,
                Encoder = draft.Encoder,
                QualityPreset = draft.QualityPreset,
            },
        };
        VRRecorderSettingsContract.Validate(updated);
        await _settings
            .SaveAsync(updated, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<VRRecorderSettings> LoadValidatedAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _settings
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (settings is null)
        {
            throw new InvalidDataException(
                "The settings store returned no settings document.");
        }

        VRRecorderSettingsContract.Validate(settings);
        return settings;
    }

    private static DesktopRecordingSettingsDraft Project(
        VRRecorderSettings settings) =>
        new(
            settings.Recording.SelfTimerSeconds,
            settings.Recording.AutoStopSeconds,
            settings.Recording.ResolutionChangePolicy,
            settings.Video.FrameRate,
            settings.Video.Encoder,
            settings.Video.QualityPreset);

    private static void ValidateDraft(DesktopRecordingSettingsDraft draft)
    {
        if (!SelfTimerChoices.Contains(draft.SelfTimerSeconds))
        {
            throw InvalidChoice("self timer");
        }

        if (!AutoStopChoices.Contains(draft.AutoStopSeconds))
        {
            throw InvalidChoice("auto stop");
        }

        if (!ResolutionPolicyChoices.Contains(draft.ResolutionChangePolicy))
        {
            throw InvalidChoice("resolution change policy");
        }

        if (!FrameRateChoices.Contains(draft.FrameRate))
        {
            throw InvalidChoice("frame rate");
        }

        if (!EncoderChoices.Contains(draft.Encoder))
        {
            throw InvalidChoice("encoder");
        }

        if (!QualityChoices.Contains(draft.QualityPreset))
        {
            throw InvalidChoice("quality preset");
        }
    }

    private static InvalidDataException InvalidChoice(string setting) =>
        new($"The desktop {setting} choice is not supported.");
}
