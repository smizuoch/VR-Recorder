using VRRecorder.Domain.Audio;

namespace VRRecorder.Domain.Tests.Audio;

public sealed class AudioRoutingRampTests
{
    [Fact]
    public void MicOffRampsOnlyMicrophoneGainToZeroOverTenMilliseconds()
    {
        var ramp = AudioRoutingRamp.Create(
            AudioRouting.Mixed,
            AudioRouting.DesktopOnly,
            sampleRate: 48_000);

        Assert.Equal(480, ramp.LengthSamples);
        Assert.Equal(new AudioGains(1, 1), ramp.AtSample(0));

        var halfway = ramp.AtSample(240);
        Assert.Equal(1, halfway.Desktop);
        Assert.Equal(0.5, halfway.Microphone, precision: 10);

        Assert.Equal(new AudioGains(1, 0), ramp.AtSample(480));
    }
}
