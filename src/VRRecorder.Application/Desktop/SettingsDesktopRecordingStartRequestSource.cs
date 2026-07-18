using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Desktop;

public sealed class SettingsDesktopRecordingStartRequestSource
    : IDesktopRecordingStartRequestSource
{
    private readonly ISettingsStore _settings;
    private readonly RecordingOutputPathResolver _outputPaths;

    public SettingsDesktopRecordingStartRequestSource(
        ISettingsStore settings,
        IDefaultOutputPathProvider defaultOutputPaths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _outputPaths = new RecordingOutputPathResolver(defaultOutputPaths);
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
            return Map(settings, cancellationToken);
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

    private DesktopRecordingStartRequest Map(
        VRRecorderSettings settings,
        CancellationToken cancellationToken)
    {
        VRRecorderSettingsContract.Validate(settings);
        var selfTimer = SelfTimer.FromSeconds(
            settings.Recording.SelfTimerSeconds);
        var autoStop = MapAutoStop(settings.Recording.AutoStopSeconds);
        var frameRate = new FrameRate(settings.Video.FrameRate);
        var media = MapMedia(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = _outputPaths.Resolve(
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

    private static RecordingDuration MapAutoStop(int? seconds) =>
        seconds is { } finiteSeconds
            ? RecordingDuration.FromSeconds(finiteSeconds)
            : RecordingDuration.Infinite;

    private static RecordingMediaConfiguration MapMedia(
        VRRecorderSettings settings)
    {
        var unidentifiedHardware = RecordingMediaConfiguration.CreateDefault();
        return new RecordingMediaConfiguration(
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
    }

}
