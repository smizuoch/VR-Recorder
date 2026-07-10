using VRRecorder.Domain.Audio;

namespace VRRecorder.Domain.Tests.Audio;

public sealed class AudioMixerTests
{
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
