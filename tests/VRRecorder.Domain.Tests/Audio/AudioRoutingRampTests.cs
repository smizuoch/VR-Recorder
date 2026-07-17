using VRRecorder.Domain.Audio;

namespace VRRecorder.Domain.Tests.Audio;

public sealed class AudioRoutingRampTests
{
    [Theory]
    [InlineData(AudioRouting.Mixed, 1, 1)]
    [InlineData(AudioRouting.DesktopOnly, 1, 0)]
    [InlineData(AudioRouting.MicOnly, 0, 1)]
    [InlineData(AudioRouting.Muted, 0, 0)]
    public void MapsEveryRoutingAndClampsPastTheRampEnd(
        AudioRouting routing,
        double expectedDesktop,
        double expectedMicrophone)
    {
        var ramp = AudioRoutingRamp.Create(
            AudioRouting.Muted,
            routing,
            sampleRate: 50);

        Assert.Equal(1, ramp.LengthSamples);
        Assert.Equal(
            new AudioGains(expectedDesktop, expectedMicrophone),
            ramp.AtSample(2));
    }

    [Fact]
    public void RejectsUnknownRoutingAndInvalidOffsetsOrSampleRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioRoutingRamp.Create(
                (AudioRouting)int.MaxValue,
                AudioRouting.Mixed,
                48_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioRoutingRamp.Create(
                AudioRouting.Mixed,
                (AudioRouting)int.MaxValue,
                48_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioRoutingRamp.Create(
                new AudioGains(1, 1),
                new AudioGains(0, 0),
                0));

        var ramp = AudioRoutingRamp.Create(
            AudioRouting.Mixed,
            AudioRouting.Muted,
            48_000);
        Assert.Throws<ArgumentOutOfRangeException>(() => ramp.AtSample(-1));
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0, 0)]
    [InlineData(0, 0, double.NegativeInfinity, 0)]
    [InlineData(0, 0, 0, double.NaN)]
    public void RejectsNonFiniteStartAndEndGains(
        double fromDesktop,
        double fromMicrophone,
        double toDesktop,
        double toMicrophone)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioRoutingRamp.Create(
                new AudioGains(fromDesktop, fromMicrophone),
                new AudioGains(toDesktop, toMicrophone),
                48_000));
    }

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

    [Fact]
    public void MicOnRampsOnlyMicrophoneGainBackOverTenMilliseconds()
    {
        var ramp = AudioRoutingRamp.Create(
            AudioRouting.DesktopOnly,
            AudioRouting.Mixed,
            sampleRate: 48_000);

        Assert.Equal(new AudioGains(1, 0), ramp.AtSample(0));

        var halfway = ramp.AtSample(240);
        Assert.Equal(1, halfway.Desktop);
        Assert.Equal(0.5, halfway.Microphone, precision: 10);

        Assert.Equal(new AudioGains(1, 1), ramp.AtSample(480));
    }
}
