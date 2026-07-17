using VRRecorder.Domain.Audio;

namespace VRRecorder.Domain.Tests.Audio;

public sealed class AudioMixerTests
{
    [Fact]
    public void InterleavedRoutingUsesPerFrameRampForEveryChannel()
    {
        var ramp = AudioRoutingRamp.Create(
            new AudioGains(1, 0),
            new AudioGains(0, 1),
            sampleRate: 100);
        var desktop = new[] { 1f, 2f, 3f, 4f };
        var microphone = new[] { 10f, 20f, 30f, 40f };
        var expected = new[] { 1f, 2f, 30f, 40f };

        var output = AudioMixer.MixInterleaved(
            desktop,
            microphone,
            scheduledFrameCount: 2,
            channelCount: 2,
            ramp,
            rampStartFrame: 0,
            AudioInputAvailability.All);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void InterleavedRoutingZeroFillsUnavailableInputs()
    {
        var ramp = AudioRoutingRamp.Create(
            AudioRouting.Mixed,
            AudioRouting.Mixed,
            sampleRate: 48_000);
        var available = new[] { 0.25f, 0.5f };

        Assert.Equal(
            available,
            AudioMixer.MixInterleaved(
                desktop: [],
                microphone: available,
                scheduledFrameCount: 1,
                channelCount: 2,
                ramp,
                rampStartFrame: 0,
                AudioInputAvailability.Microphone));
        Assert.Equal(
            available,
            AudioMixer.MixInterleaved(
                desktop: available,
                microphone: [],
                scheduledFrameCount: 1,
                channelCount: 2,
                ramp,
                rampStartFrame: 0,
                AudioInputAvailability.Desktop));
    }

    [Fact]
    public void RejectsEveryInvalidMixerBoundary()
    {
        var ramp = AudioRoutingRamp.Create(
            AudioRouting.Mixed,
            AudioRouting.Muted,
            48_000);

        Assert.Throws<ArgumentNullException>(() =>
            AudioMixer.Mix(null!, [], new AudioGains(1, 1)));
        Assert.Throws<ArgumentNullException>(() =>
            AudioMixer.Mix([], null!, new AudioGains(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.Mix([], [], -1, new AudioGains(1, 1),
                AudioInputAvailability.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.Mix([], [], 0, new AudioGains(1, 1),
                (AudioInputAvailability)int.MaxValue));
        Assert.Throws<ArgumentException>(() =>
            AudioMixer.Mix([], [], 1, new AudioGains(1, 1),
                AudioInputAvailability.Desktop));
        Assert.Throws<ArgumentException>(() =>
            AudioMixer.Mix([], [], 1, new AudioGains(1, 1),
                AudioInputAvailability.Microphone));

        Assert.Throws<ArgumentNullException>(() =>
            AudioMixer.MixInterleaved(
                null!, [], 0, 1, ramp, 0, AudioInputAvailability.None));
        Assert.Throws<ArgumentNullException>(() =>
            AudioMixer.MixInterleaved(
                [], null!, 0, 1, ramp, 0, AudioInputAvailability.None));
        Assert.Throws<ArgumentNullException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 0, 1, null!, 0, AudioInputAvailability.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.MixInterleaved(
                [], [], -1, 1, ramp, 0, AudioInputAvailability.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 0, 0, ramp, 0, AudioInputAvailability.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 0, 1, ramp, -1, AudioInputAvailability.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 0, 1, ramp, 0,
                (AudioInputAvailability)int.MaxValue));
        Assert.Throws<OverflowException>(() =>
            AudioMixer.MixInterleaved(
                [], [], int.MaxValue, 2, ramp, 0,
                AudioInputAvailability.None));
        Assert.Throws<ArgumentException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 1, 1, ramp, 0,
                AudioInputAvailability.Desktop));
        Assert.Throws<ArgumentException>(() =>
            AudioMixer.MixInterleaved(
                [], [], 1, 1, ramp, 0,
                AudioInputAvailability.Microphone));
    }

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
