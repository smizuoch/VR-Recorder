using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;

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
    private readonly RecordingOutputPathResolver _outputPaths;
    private readonly ILegalBundleOutputMirror _legalBundleMirror;

    public DesktopRecordingSettingsController(
        ISettingsStore settings,
        RecordingOutputPathResolver outputPaths,
        ILegalBundleOutputMirror legalBundleMirror)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(outputPaths);
        ArgumentNullException.ThrowIfNull(legalBundleMirror);
        _settings = settings;
        _outputPaths = outputPaths;
        _legalBundleMirror = legalBundleMirror;
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
        DesktopRecordingSettingsDraft original,
        DesktopRecordingSettingsDraft edited,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edited);
        _ = ValidateDraft(original);
        var resolvedOutputPath = ValidateDraft(edited);
        var current = await LoadValidatedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                original.OutputFolder,
                edited.OutputFolder,
                StringComparison.Ordinal))
        {
            await _legalBundleMirror
                .MirrorAsync(resolvedOutputPath, cancellationToken)
                .ConfigureAwait(false);
        }

        var updated = current with
        {
            Recording = current.Recording with
            {
                OutputFolder = Merge(
                    original.OutputFolder,
                    edited.OutputFolder,
                    current.Recording.OutputFolder),
                SelfTimerSeconds = Merge(
                    original.SelfTimerSeconds,
                    edited.SelfTimerSeconds,
                    current.Recording.SelfTimerSeconds),
                AutoStopSeconds = Merge(
                    original.AutoStopSeconds,
                    edited.AutoStopSeconds,
                    current.Recording.AutoStopSeconds),
                ResolutionChangePolicy = Merge(
                    original.ResolutionChangePolicy,
                    edited.ResolutionChangePolicy,
                    current.Recording.ResolutionChangePolicy),
            },
            Video = current.Video with
            {
                FrameRate = Merge(
                    original.FrameRate,
                    edited.FrameRate,
                    current.Video.FrameRate),
                Encoder = Merge(
                    original.Encoder,
                    edited.Encoder,
                    current.Video.Encoder),
                QualityPreset = Merge(
                    original.QualityPreset,
                    edited.QualityPreset,
                    current.Video.QualityPreset),
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
            settings.Recording.OutputFolder,
            settings.Recording.SelfTimerSeconds,
            settings.Recording.AutoStopSeconds,
            settings.Recording.ResolutionChangePolicy,
            settings.Video.FrameRate,
            settings.Video.Encoder,
            settings.Video.QualityPreset);

    public OutputPath ResolveOutputPath(string configuredPath) =>
        _outputPaths.Resolve(configuredPath);

    private OutputPath ValidateDraft(DesktopRecordingSettingsDraft draft)
    {
        var outputPath = _outputPaths.Resolve(draft.OutputFolder);
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

        try
        {
            _ = new FrameRate(draft.FrameRate);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "The desktop frame rate is not supported.",
                exception);
        }

        if (!EncoderChoices.Contains(draft.Encoder))
        {
            throw InvalidChoice("encoder");
        }

        if (!QualityChoices.Contains(draft.QualityPreset))
        {
            throw InvalidChoice("quality preset");
        }

        return outputPath;
    }

    private static InvalidDataException InvalidChoice(string setting) =>
        new($"The desktop {setting} choice is not supported.");

    private static T Merge<T>(T original, T edited, T current) =>
        EqualityComparer<T>.Default.Equals(original, edited)
            ? current
            : edited;
}
