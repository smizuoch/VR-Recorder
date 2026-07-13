using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Setup;

public sealed class EncoderSelfTestFirstRunSetupProbe : IFirstRunSetupProbe
{
    private readonly ISettingsStore _settings;
    private readonly EncoderSelector _encoders;

    public EncoderSelfTestFirstRunSetupProbe(
        ISettingsStore settings,
        IEncoderProbe encoders)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(encoders);
        _settings = settings;
        _encoders = new EncoderSelector(encoders);
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.EncoderSelfTest)
        {
            return false;
        }

        var settings = await _settings.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            _ = await _encoders.SelectAsync(
                    settings.Video.Encoder,
                    GpuVendor.Unknown,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (EncoderUnavailableException)
        {
            return false;
        }
    }
}
