using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
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
    private static readonly IReadOnlyList<AudioRouting> AudioRoutingChoices =
        Array.AsReadOnly(
        [
            AudioRouting.Mixed,
            AudioRouting.DesktopOnly,
            AudioRouting.MicOnly,
            AudioRouting.Muted,
        ]);

    private readonly ISettingsStore _settings;
    private readonly RecordingOutputPathResolver _outputPaths;
    private readonly ILegalBundleOutputMirror _legalBundleMirror;
    private readonly IAudioEndpointCatalog? _audioEndpoints;

    public DesktopRecordingSettingsController(
        ISettingsStore settings,
        RecordingOutputPathResolver outputPaths,
        ILegalBundleOutputMirror legalBundleMirror)
        : this(settings, outputPaths, legalBundleMirror, audioEndpoints: null)
    {
    }

    public DesktopRecordingSettingsController(
        ISettingsStore settings,
        RecordingOutputPathResolver outputPaths,
        ILegalBundleOutputMirror legalBundleMirror,
        IAudioEndpointCatalog? audioEndpoints)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(outputPaths);
        ArgumentNullException.ThrowIfNull(legalBundleMirror);
        _settings = settings;
        _outputPaths = outputPaths;
        _legalBundleMirror = legalBundleMirror;
        _audioEndpoints = audioEndpoints;
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

    public static IReadOnlyList<AudioRouting> SupportedAudioRoutings =>
        AudioRoutingChoices;

    public async Task<DesktopRecordingSettingsDraft> LoadAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadValidatedAsync(cancellationToken)
            .ConfigureAwait(false);
        return Project(settings);
    }

    public async Task<DesktopAudioEndpointOptions> LoadAudioEndpointOptionsAsync(
        DesktopRecordingSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _ = ValidateDraft(draft);
        if (_audioEndpoints is null)
        {
            return new DesktopAudioEndpointOptions(
                [new AudioEndpointOption(
                    draft.DesktopEndpointId,
                    draft.DesktopEndpointId)],
                [new AudioEndpointOption(
                    draft.MicrophoneEndpointId,
                    draft.MicrophoneEndpointId)]);
        }

        var desktop = await _audioEndpoints
            .GetActiveAsync(AudioInput.Desktop, cancellationToken)
            .ConfigureAwait(false);
        var microphone = await _audioEndpoints
            .GetActiveAsync(AudioInput.Microphone, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(microphone);
        return new DesktopAudioEndpointOptions(
            MergeEndpointOptions(draft.DesktopEndpointId, desktop),
            MergeEndpointOptions(draft.MicrophoneEndpointId, microphone));
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
            Audio = current.Audio with
            {
                Routing = Merge(
                    original.AudioRouting,
                    edited.AudioRouting,
                    current.Audio.Routing),
                DesktopEndpointId = Merge(
                    original.DesktopEndpointId,
                    edited.DesktopEndpointId,
                    current.Audio.DesktopEndpointId),
                MicrophoneEndpointId = Merge(
                    original.MicrophoneEndpointId,
                    edited.MicrophoneEndpointId,
                    current.Audio.MicrophoneEndpointId),
                DesktopGainDb = Merge(
                    original.DesktopGainDb,
                    edited.DesktopGainDb,
                    current.Audio.DesktopGainDb),
                MicrophoneGainDb = Merge(
                    original.MicrophoneGainDb,
                    edited.MicrophoneGainDb,
                    current.Audio.MicrophoneGainDb),
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
            settings.Video.QualityPreset,
            settings.Audio.Routing,
            settings.Audio.DesktopGainDb,
            settings.Audio.MicrophoneGainDb,
            settings.Audio.DesktopEndpointId,
            settings.Audio.MicrophoneEndpointId);

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

        if (!AudioRoutingChoices.Contains(draft.AudioRouting))
        {
            throw InvalidChoice("audio routing");
        }

        ValidateEndpointId(draft.DesktopEndpointId, "desktop endpoint");
        ValidateEndpointId(
            draft.MicrophoneEndpointId,
            "microphone endpoint");

        ValidateGain(draft.DesktopGainDb, "desktop gain");
        ValidateGain(draft.MicrophoneGainDb, "microphone gain");

        return outputPath;
    }

    private static InvalidDataException InvalidChoice(string setting) =>
        new($"The desktop {setting} choice is not supported.");

    private static T Merge<T>(T original, T edited, T current) =>
        EqualityComparer<T>.Default.Equals(original, edited)
            ? current
            : edited;

    private static void ValidateGain(double value, string setting)
    {
        if (!double.IsFinite(value) ||
            value is < RecordingMediaConfiguration.MinimumInputGainDb or
                > RecordingMediaConfiguration.MaximumInputGainDb)
        {
            throw InvalidChoice(setting);
        }
    }

    private static void ValidateEndpointId(string value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw InvalidChoice(setting);
        }
    }

    private static IReadOnlyList<AudioEndpointOption> MergeEndpointOptions(
        string selectedId,
        IReadOnlyList<AudioEndpointOption> active)
    {
        var unique = new Dictionary<string, AudioEndpointOption>(
            StringComparer.Ordinal);
        foreach (var option in active)
        {
            ArgumentNullException.ThrowIfNull(option);
            unique.TryAdd(option.Id, option);
        }

        var selected = unique.Remove(selectedId, out var current)
            ? current
            : new AudioEndpointOption(selectedId, selectedId);
        return [selected, .. unique.Values];
    }
}
