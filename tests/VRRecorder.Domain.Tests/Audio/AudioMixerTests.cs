using VRRecorder.Domain.Audio;

namespace VRRecorder.Domain.Tests.Audio;

public sealed class AudioMixerTests
{
    [Fact]
    public void LostMicrophoneUsesSilenceAndPreservesScheduledSampleCount()
    {
        const int sampleCount = 480;
        var desktop = Enumerable.Repeat(0.25f, sampleCount).ToArray();

        var output = AudioMixer.Mix(
            desktop,
            microphone: [],
            sampleCount,
            new AudioGains(Desktop: 1, Microphone: 1),
            AudioInputAvailability.Desktop);

        Assert.Equal(sampleCount, output.Length);
        Assert.Equal(desktop, output);
    }

    [Fact]
    public void LostDesktopUsesSilenceAndPreservesScheduledSampleCount()
    {
        const int sampleCount = 480;
        var microphone = Enumerable.Repeat(0.5f, sampleCount).ToArray();

        var output = AudioMixer.Mix(
            desktop: [],
            microphone,
            sampleCount,
            new AudioGains(Desktop: 1, Microphone: 0.5),
            AudioInputAvailability.Microphone);

        Assert.Equal(sampleCount, output.Length);
        Assert.All(output, sample => Assert.Equal(0.25f, sample));
    }

    [Fact]
    public void RecoveredMicrophoneRestoresOnlyItsContribution()
    {
        const int sampleCount = 480;
        var desktop = Enumerable.Repeat(0.25f, sampleCount).ToArray();
        var microphone = Enumerable.Repeat(0.5f, sampleCount).ToArray();
        var gains = new AudioGains(Desktop: 1, Microphone: 0.5);
        var withoutMicrophone = AudioMixer.Mix(
            desktop,
            microphone: [],
            sampleCount,
            gains,
            AudioInputAvailability.Desktop);

        var recovered = AudioMixer.Mix(
            desktop,
            microphone,
            sampleCount,
            gains,
            AudioInputAvailability.All);

        Assert.Equal(sampleCount, recovered.Length);
        for (var index = 0; index < sampleCount; index++)
        {
            Assert.Equal(0.25f, withoutMicrophone[index]);
            Assert.Equal(0.5f, recovered[index]);
        }
    }

    [Fact]
    public void MutedRoutingRemovesDesktopAndMicrophoneFrequenciesButKeepsTimeline()
    {
        const int sampleRate = 48_000;
        const int sampleCount = 480;
        var desktop = GenerateSine(440, sampleRate, sampleCount);
        var microphone = GenerateSine(880, sampleRate, sampleCount);
        var muteRamp = AudioRoutingRamp.Create(
            AudioRouting.Mixed,
            AudioRouting.Muted,
            sampleRate);

        var output = AudioMixer.Mix(
            desktop,
            microphone,
            muteRamp.AtSample(muteRamp.LengthSamples));

        Assert.Equal(sampleCount, output.Length);
        Assert.All(output, sample => Assert.Equal(0, sample));
    }

    private static float[] GenerateSine(
        double frequency,
        int sampleRate,
        int sampleCount) =>
        Enumerable.Range(0, sampleCount)
            .Select(index => (float)Math.Sin(
                2 * Math.PI * frequency * index / sampleRate))
            .ToArray();
}
