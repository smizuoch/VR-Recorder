using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public static class AudioDeviceRecoveryPolicy
{
    private static readonly TimeSpan DesktopRediscoveryBudget =
        TimeSpan.FromSeconds(5);

    public static AudioEndpointRediscoveryRequest ForDesktopLoss() =>
        new(
            AudioInput.Desktop,
            AudioEndpointRole.DefaultRender,
            DesktopRediscoveryBudget);
}
