using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.IntegrationTests.Audio;

public sealed class AudioSessionIntegrationTests
{
    private const int SampleRate = AudioSessionService.RequiredSampleRate;

    [Fact]
    public void MixedRoutingContributesDesktopAndMicrophoneAt48Khz()
    {
        const int sampleCount = AudioSessionService.MaxScheduledSampleCount;
        var desktop = GenerateSine(440, startSample: 0, sampleCount, amplitude: 0.25);
        var microphone = GenerateSine(880, startSample: 0, sampleCount, amplitude: 0.25);
        var session = CreateSession(AudioRouting.Mixed);

        var mixed = session.Process(new ScheduledAudioBuffer(
            StartSample: 0,
            SampleRate,
            sampleCount,
            desktop,
            microphone,
            AudioInputAvailability.All));

        Assert.Equal(0, mixed.StartSample);
        Assert.Equal(SampleRate, mixed.SampleRate);
        Assert.Equal(sampleCount, mixed.Samples.Length);
        Assert.True(ProjectionMagnitude(mixed.Samples, 440) > 0.2);
        Assert.True(ProjectionMagnitude(mixed.Samples, 880) > 0.2);
        AssertSamples(
            desktop.Zip(microphone, (left, right) => left + right).ToArray(),
            mixed.Samples);
    }

    [Fact]
    public void MicOffAndOnRampOnlyMicrophoneContributionOverTenMilliseconds()
    {
        const int blockSize = 480;
        var session = CreateSession(AudioRouting.Mixed);
        ProcessSines(session, startSample: 0, blockSize);

        session.SetRouting(AudioRouting.DesktopOnly);
        var micOff = ProcessSines(session, startSample: blockSize, blockSize);
        var offDesktop = GenerateSine(440, blockSize, blockSize, amplitude: 0.25);
        var offMicrophone = GenerateSine(880, blockSize, blockSize, amplitude: 0.25);
        AssertRamp(micOff.Samples, offDesktop, offMicrophone, gainAt: index =>
            1 - ((double)index / blockSize));

        var desktopOnly = ProcessSines(session, startSample: blockSize * 2, blockSize);
        AssertSamples(
            GenerateSine(440, blockSize * 2, blockSize, amplitude: 0.25),
            desktopOnly.Samples);

        session.SetRouting(AudioRouting.Mixed);
        var micOn = ProcessSines(session, startSample: blockSize * 3, blockSize);
        var onDesktop = GenerateSine(440, blockSize * 3, blockSize, amplitude: 0.25);
        var onMicrophone = GenerateSine(880, blockSize * 3, blockSize, amplitude: 0.25);
        AssertRamp(micOn.Samples, onDesktop, onMicrophone, gainAt: index =>
            (double)index / blockSize);

        var mixed = ProcessSines(session, startSample: blockSize * 4, blockSize);
        AssertSamples(
            GenerateSine(440, blockSize * 4, blockSize, amplitude: 0.25)
                .Zip(
                    GenerateSine(880, blockSize * 4, blockSize, amplitude: 0.25),
                    (left, right) => left + right)
                .ToArray(),
            mixed.Samples);
    }

    [Fact]
    public void MutedRoutingKeepsTheScheduledTimelineWithSilence()
    {
        const int blockSize = 480;
        var session = CreateSession(AudioRouting.Mixed);
        session.SetRouting(AudioRouting.Muted);

        var transition = ProcessSines(session, startSample: 0, blockSize);
        var muted = ProcessSines(session, startSample: blockSize, blockSize);

        Assert.Equal(blockSize, transition.Samples.Length);
        Assert.Equal(blockSize, muted.StartSample);
        Assert.Equal(blockSize, muted.Samples.Length);
        Assert.All(muted.Samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void LostMicrophoneContinuesDesktopAndEmitsTypedWarning()
    {
        const int blockSize = 480;
        var events = new RecordingAudioSessionEventSink();
        var rediscovery = new RecordingRediscoveryScheduler();
        var session = new AudioSessionService(
            AudioRouting.Mixed,
            rediscovery,
            events);
        var desktop = GenerateSine(440, startSample: 0, blockSize, amplitude: 0.25);

        var mixed = session.Process(new ScheduledAudioBuffer(
            StartSample: 0,
            SampleRate,
            blockSize,
            desktop,
            MicrophoneSamples: [],
            AudioInputAvailability.Desktop));

        AssertSamples(desktop, mixed.Samples);
        var warning = Assert.Single(events.Warnings);
        Assert.Equal(AudioSessionWarningKind.InputUnavailable, warning.Kind);
        Assert.Equal(AudioInput.Microphone, warning.Input);
        Assert.Equal(0, warning.SamplePosition);
        Assert.Empty(rediscovery.Requests);
    }

    [Fact]
    public void LostDesktopSchedulesBoundedRediscoveryOnceThenUsesSilenceAndFadesRecovery()
    {
        const int blockSize = 480;
        var events = new RecordingAudioSessionEventSink();
        var rediscovery = new RecordingRediscoveryScheduler();
        var session = new AudioSessionService(
            AudioRouting.DesktopOnly,
            rediscovery,
            events);
        ProcessSines(session, startSample: 0, blockSize);

        var lost = session.Process(new ScheduledAudioBuffer(
            StartSample: blockSize,
            SampleRate,
            blockSize,
            DesktopSamples: [],
            GenerateSine(880, blockSize, blockSize, amplitude: 0.25),
            AudioInputAvailability.Microphone));
        var stillLost = session.Process(new ScheduledAudioBuffer(
            StartSample: blockSize * 2,
            SampleRate,
            blockSize,
            DesktopSamples: [],
            GenerateSine(880, blockSize * 2, blockSize, amplitude: 0.25),
            AudioInputAvailability.Microphone));

        Assert.All(lost.Samples, sample => Assert.Equal(0, sample));
        Assert.All(stillLost.Samples, sample => Assert.Equal(0, sample));
        var request = Assert.Single(rediscovery.Requests);
        Assert.Equal(AudioDeviceRecoveryPolicy.ForDesktopLoss(), request);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Budget);
        Assert.Contains(events.Statuses, status =>
            status.Kind == AudioSessionStatusKind.EndpointRediscoveryScheduled &&
            status.Input == AudioInput.Desktop &&
            status.SamplePosition == blockSize);

        var recoveredDesktop = GenerateSine(
            440,
            blockSize * 3,
            blockSize,
            amplitude: 0.25);
        var recovered = session.Process(new ScheduledAudioBuffer(
            StartSample: blockSize * 3,
            SampleRate,
            blockSize,
            recoveredDesktop,
            GenerateSine(880, blockSize * 3, blockSize, amplitude: 0.25),
            AudioInputAvailability.All));
        AssertRamp(
            recovered.Samples,
            new float[blockSize],
            recoveredDesktop,
            gainAt: index => (double)index / blockSize);
        Assert.Contains(events.Statuses, status =>
            status.Kind == AudioSessionStatusKind.InputRecovered &&
            status.Input == AudioInput.Desktop &&
            status.SamplePosition == blockSize * 3);

        var steady = ProcessSines(
            session,
            startSample: blockSize * 4,
            blockSize);
        AssertSamples(
            GenerateSine(440, blockSize * 4, blockSize, amplitude: 0.25),
            steady.Samples);
    }

    [Fact]
    public void RediscoveryFailureWarnsAndStillReturnsScheduledSilence()
    {
        const int blockSize = 480;
        var events = new RecordingAudioSessionEventSink();
        var session = new AudioSessionService(
            AudioRouting.DesktopOnly,
            new ThrowingRediscoveryScheduler(),
            events);

        var mixed = session.Process(new ScheduledAudioBuffer(
            StartSample: 0,
            SampleRate,
            blockSize,
            DesktopSamples: [],
            GenerateSine(880, startSample: 0, blockSize, amplitude: 0.25),
            AudioInputAvailability.Microphone));

        Assert.Equal(blockSize, mixed.Samples.Length);
        Assert.All(mixed.Samples, sample => Assert.Equal(0, sample));
        Assert.Contains(events.Warnings, warning =>
            warning.Kind == AudioSessionWarningKind.EndpointRediscoveryFailed &&
            warning.Input == AudioInput.Desktop);
    }

    [Fact]
    public void InvalidBufferDoesNotAdvanceTheSessionTimeline()
    {
        const int blockSize = 480;
        var session = CreateSession(AudioRouting.Mixed);
        ProcessSines(session, startSample: 0, blockSize);

        var mismatch = new ScheduledAudioBuffer(
            StartSample: blockSize,
            SampleRate,
            blockSize,
            DesktopSamples: new float[blockSize - 1],
            MicrophoneSamples: new float[blockSize],
            AudioInputAvailability.All);
        Assert.Throws<ArgumentException>(() => session.Process(mismatch));

        var valid = ProcessSines(session, startSample: blockSize, blockSize);
        Assert.Equal(blockSize, valid.StartSample);
    }

    [Theory]
    [InlineData(44_100, 480)]
    [InlineData(SampleRate, 4_801)]
    public void UnsupportedOrOversizedBuffersFailClosed(
        int sampleRate,
        int sampleCount)
    {
        var session = CreateSession(AudioRouting.Mixed);
        var buffer = new ScheduledAudioBuffer(
            StartSample: 0,
            sampleRate,
            sampleCount,
            new float[sampleCount],
            new float[sampleCount],
            AudioInputAvailability.All);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Process(buffer));
    }

    [Fact]
    public void ContradictoryAvailabilityAndNonFiniteSamplesFailClosed()
    {
        const int blockSize = 480;
        var session = CreateSession(AudioRouting.Mixed);
        var contradictory = new ScheduledAudioBuffer(
            StartSample: 0,
            SampleRate,
            blockSize,
            new float[blockSize],
            new float[blockSize],
            AudioInputAvailability.Desktop);
        Assert.Throws<ArgumentException>(() => session.Process(contradictory));

        var desktop = new float[blockSize];
        desktop[20] = float.NaN;
        var nonFinite = contradictory with
        {
            DesktopSamples = desktop,
            MicrophoneSamples = [],
        };
        Assert.Throws<ArgumentException>(() => session.Process(nonFinite));
    }

    private static AudioSessionService CreateSession(AudioRouting routing) =>
        new(
            routing,
            new RecordingRediscoveryScheduler(),
            new RecordingAudioSessionEventSink());

    private static MixedAudioBuffer ProcessSines(
        AudioSessionService session,
        long startSample,
        int sampleCount) =>
        session.Process(new ScheduledAudioBuffer(
            startSample,
            SampleRate,
            sampleCount,
            GenerateSine(440, startSample, sampleCount, amplitude: 0.25),
            GenerateSine(880, startSample, sampleCount, amplitude: 0.25),
            AudioInputAvailability.All));

    private static float[] GenerateSine(
        double frequency,
        long startSample,
        int sampleCount,
        double amplitude) =>
        Enumerable.Range(0, sampleCount)
            .Select(index => (float)(amplitude * Math.Sin(
                2 * Math.PI * frequency * (startSample + index) / SampleRate)))
            .ToArray();

    private static double ProjectionMagnitude(
        IReadOnlyList<float> samples,
        double frequency)
    {
        var sine = 0d;
        var cosine = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var angle = 2 * Math.PI * frequency * index / SampleRate;
            sine += samples[index] * Math.Sin(angle);
            cosine += samples[index] * Math.Cos(angle);
        }

        return 2 * Math.Sqrt((sine * sine) + (cosine * cosine)) /
               samples.Count;
    }

    private static void AssertRamp(
        IReadOnlyList<float> actual,
        IReadOnlyList<float> constantContribution,
        IReadOnlyList<float> rampedContribution,
        Func<int, double> gainAt)
    {
        Assert.Equal(constantContribution.Count, actual.Count);
        Assert.Equal(rampedContribution.Count, actual.Count);
        for (var index = 0; index < actual.Count; index++)
        {
            var expected = constantContribution[index] +
                           (rampedContribution[index] * gainAt(index));
            Assert.Equal(expected, actual[index], precision: 6);
        }
    }

    private static void AssertSamples(
        IReadOnlyList<float> expected,
        IReadOnlyList<float> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], actual[index], precision: 6);
        }
    }

    private sealed class RecordingRediscoveryScheduler :
        IAudioEndpointRediscoveryScheduler
    {
        public List<AudioEndpointRediscoveryRequest> Requests { get; } = [];

        public void Schedule(AudioEndpointRediscoveryRequest request) =>
            Requests.Add(request);
    }

    private sealed class ThrowingRediscoveryScheduler :
        IAudioEndpointRediscoveryScheduler
    {
        public void Schedule(AudioEndpointRediscoveryRequest request) =>
            throw new InvalidOperationException("rediscovery unavailable");
    }

    private sealed class RecordingAudioSessionEventSink : IAudioSessionEventSink
    {
        public List<AudioSessionWarning> Warnings { get; } = [];

        public List<AudioSessionStatus> Statuses { get; } = [];

        public void Publish(AudioSessionWarning warning) => Warnings.Add(warning);

        public void Publish(AudioSessionStatus status) => Statuses.Add(status);
    }
}
