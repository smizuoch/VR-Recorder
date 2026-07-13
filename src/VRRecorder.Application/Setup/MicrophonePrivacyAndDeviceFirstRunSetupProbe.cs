using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Setup;

public sealed class MicrophonePrivacyAndDeviceFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private const string UnconfirmedDefaultEndpointId = "default-capture";
    private readonly ISettingsStore _settings;
    private readonly IAudioEndpointCatalog _endpoints;
    private readonly IMicrophonePrivacyAccess _privacy;

    public MicrophonePrivacyAndDeviceFirstRunSetupProbe(
        ISettingsStore settings,
        IAudioEndpointCatalog endpoints,
        IMicrophonePrivacyAccess privacy)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(privacy);
        _settings = settings;
        _endpoints = endpoints;
        _privacy = privacy;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.MicrophonePrivacyAndDevice)
        {
            return false;
        }

        if (!await _privacy.IsAllowedAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        var settings = await _settings.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(settings);
        var selectedId = settings.Audio.MicrophoneEndpointId;
        if (string.Equals(
                selectedId,
                UnconfirmedDefaultEndpointId,
                StringComparison.Ordinal))
        {
            return false;
        }

        var active = await _endpoints
            .GetActiveAsync(AudioInput.Microphone, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(active);
        return active.Any(endpoint => string.Equals(
            endpoint.Id,
            selectedId,
            StringComparison.Ordinal));
    }
}
