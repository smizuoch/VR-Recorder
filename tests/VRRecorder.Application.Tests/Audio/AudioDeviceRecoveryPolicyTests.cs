using VRRecorder.Application.Audio;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.Audio;

public sealed class AudioDeviceRecoveryPolicyTests
{
    [Fact]
    public void DesktopLossRequestsDefaultRenderRediscoveryForFiveSeconds()
    {
        var request = AudioDeviceRecoveryPolicy.ForDesktopLoss();

        Assert.Equal(AudioInput.Desktop, request.Input);
        Assert.Equal(AudioEndpointRole.DefaultRender, request.Role);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Budget);
    }
}
