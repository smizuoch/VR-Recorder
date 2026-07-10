using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Desktop;

public sealed class SettingsDesktopRecordingStartRequestSource
    : IDesktopRecordingStartRequestSource
{
    private const string DownloadsKnownFolderToken = "knownfolder:Downloads";
    private const string KnownFolderTokenPrefix = "knownfolder:";
    private readonly ISettingsStore _settings;
    private readonly IDefaultOutputPathProvider _defaultOutputPaths;

    public SettingsDesktopRecordingStartRequestSource(
        ISettingsStore settings,
        IDefaultOutputPathProvider defaultOutputPaths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaultOutputPaths);
        _settings = settings;
        _defaultOutputPaths = defaultOutputPaths;
    }

    public async Task<DesktopRecordingStartRequest> GetAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _settings
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings is null)
        {
            throw new InvalidDataException(
                "The settings store returned no settings document.");
        }

        try
        {
            VRRecorderSettingsContract.Validate(settings);
            var selfTimer = SelfTimer.FromSeconds(
                settings.Recording.SelfTimerSeconds);
            var autoStop = settings.Recording.AutoStopSeconds is { } seconds
                ? RecordingDuration.FromSeconds(seconds)
                : RecordingDuration.Infinite;
            var frameRate = new FrameRate(settings.Video.FrameRate);
            var unidentifiedHardware = RecordingMediaConfiguration.CreateDefault();
            var media = new RecordingMediaConfiguration(
                settings.Audio.Routing,
                settings.Audio.DesktopEndpointId,
                settings.Audio.MicrophoneEndpointId,
                settings.Audio.DesktopGainDb,
                settings.Audio.MicrophoneGainDb,
                settings.Video.QualityPreset,
                unidentifiedHardware.SpoutSenderIdentity,
                unidentifiedHardware.SpoutAdapterLuid,
                unidentifiedHardware.EncoderAdapterLuid,
                unidentifiedHardware.GpuIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = ResolveOutputPath(
                settings.Recording.OutputFolder);
            cancellationToken.ThrowIfCancellationRequested();

            return new DesktopRecordingStartRequest(
                selectedServiceId: null,
                new StartRecordingCommand(
                    selfTimer,
                    autoStop,
                    outputPath,
                    frameRate,
                    settings.Video.Encoder,
                    GpuVendor.Unknown,
                    settings.Recording.ResolutionChangePolicy,
                    media));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The recording settings cannot be mapped to a safe start request.",
                exception);
        }
    }

    private OutputPath ResolveOutputPath(string configuredPath)
    {
        if (string.Equals(
                configuredPath,
                DownloadsKnownFolderToken,
                StringComparison.Ordinal))
        {
            return _defaultOutputPaths.GetDefault() ??
                   throw new InvalidDataException(
                       "The Downloads known folder could not be resolved.");
        }

        if (configuredPath.StartsWith(
                KnownFolderTokenPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The configured output known-folder token is not supported.");
        }

        if (configuredPath.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "The configured output path contains control characters.");
        }

        try
        {
            return new OutputPath(configuredPath);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The configured output path must be a safe absolute path.",
                exception);
        }
    }
}
