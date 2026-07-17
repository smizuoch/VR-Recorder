using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.IntegrationTests.Audio;

public sealed class AudioSessionIntegrationTests
{
    private const int SampleRate = AudioSessionService.RequiredSampleRate;
    private const int ChannelCount = AudioSessionService.RequiredChannelCount;

    [Fact]
    public void MixedRoutingContributesTwoInterleavedStereoInputsAt48Khz()
    {
        const int frameCount = AudioSessionService.MaxScheduledFrameCount;
        var desktop = GenerateStereoSine(
            440,
            startFrame: 0,
            frameCount,
            amplitude: 0.25);
        var microphone = GenerateStereoSine(
            880,
            startFrame: 0,
            frameCount,
            amplitude: 0.25);
        var session = CreateSession(AudioRouting.Mixed);

        var mixed = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            SampleRate,
            frameCount,
            desktop,
            microphone,
            AudioInputAvailability.All));

        Assert.Equal(0, mixed.StartFrame);
        Assert.Equal(SampleRate, mixed.SampleRate);
        Assert.Equal(ChannelCount, mixed.ChannelCount);
        Assert.Equal(frameCount * ChannelCount, mixed.InterleavedSamples.Length);
        Assert.True(LeftChannelProjectionMagnitude(
            mixed.InterleavedSamples,
            440) > 0.2);
        Assert.True(LeftChannelProjectionMagnitude(
            mixed.InterleavedSamples,
            880) > 0.2);
        AssertSamples(
            desktop.Zip(microphone, (left, right) => left + right).ToArray(),
            mixed.InterleavedSamples);
        AssertInterleavedStereoPairs(mixed.InterleavedSamples);
    }

    [Fact]
    public void MicOnlyRoutingKeepsOnlyMicrophoneInBothStereoChannels()
    {
        const int frameCount = 480;
        var session = CreateSession(AudioRouting.MicOnly);

        var mixed = ProcessSines(session, startFrame: 0, frameCount);

        AssertSamples(
            GenerateStereoSine(
                880,
                startFrame: 0,
                frameCount,
                amplitude: 0.25),
            mixed.InterleavedSamples);
        AssertInterleavedStereoPairs(mixed.InterleavedSamples);
    }

    [Fact]
    public void MicOffAndOnRampBothChannelsOverTenMilliseconds()
    {
        const int blockFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        ProcessSines(session, startFrame: 0, blockFrames);

        session.SetRouting(AudioRouting.DesktopOnly);
        var micOff = ProcessSines(
            session,
            startFrame: blockFrames,
            blockFrames);
        var offDesktop = GenerateStereoSine(
            440,
            blockFrames,
            blockFrames,
            amplitude: 0.25);
        var offMicrophone = GenerateStereoSine(
            880,
            blockFrames,
            blockFrames,
            amplitude: 0.25);
        AssertRamp(
            micOff.InterleavedSamples,
            offDesktop,
            offMicrophone,
            gainAtFrame: frame => 1 - ((double)frame / blockFrames));

        var desktopOnly = ProcessSines(
            session,
            startFrame: blockFrames * 2,
            blockFrames);
        AssertSamples(
            GenerateStereoSine(
                440,
                blockFrames * 2,
                blockFrames,
                amplitude: 0.25),
            desktopOnly.InterleavedSamples);

        session.SetRouting(AudioRouting.Mixed);
        var micOn = ProcessSines(
            session,
            startFrame: blockFrames * 3,
            blockFrames);
        var onDesktop = GenerateStereoSine(
            440,
            blockFrames * 3,
            blockFrames,
            amplitude: 0.25);
        var onMicrophone = GenerateStereoSine(
            880,
            blockFrames * 3,
            blockFrames,
            amplitude: 0.25);
        AssertRamp(
            micOn.InterleavedSamples,
            onDesktop,
            onMicrophone,
            gainAtFrame: frame => (double)frame / blockFrames);

        var mixed = ProcessSines(
            session,
            startFrame: blockFrames * 4,
            blockFrames);
        AssertSamples(
            GenerateStereoSine(
                440,
                blockFrames * 4,
                blockFrames,
                amplitude: 0.25)
                .Zip(
                    GenerateStereoSine(
                        880,
                        blockFrames * 4,
                        blockFrames,
                        amplitude: 0.25),
                    (left, right) => left + right)
                .ToArray(),
            mixed.InterleavedSamples);
    }

    [Fact]
    public void RoutingChangeDuringRampRestartsFromCurrentAudibleGain()
    {
        const int halfRampFrames = 240;
        const int fullRampFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        session.SetRouting(AudioRouting.DesktopOnly);
        ProcessSines(session, startFrame: 0, halfRampFrames);

        session.SetRouting(AudioRouting.Mixed);
        var startFrame = halfRampFrames;
        var desktop = GenerateStereoSine(
            440,
            startFrame,
            fullRampFrames,
            amplitude: 0.25);
        var microphone = GenerateStereoSine(
            880,
            startFrame,
            fullRampFrames,
            amplitude: 0.25);
        var recovered = session.Process(new ScheduledStereoAudioBuffer(
            startFrame,
            SampleRate,
            fullRampFrames,
            desktop,
            microphone,
            AudioInputAvailability.All));

        AssertRamp(
            recovered.InterleavedSamples,
            desktop,
            microphone,
            gainAtFrame: frame => 0.5 + (0.5 * frame / fullRampFrames));
    }

    [Fact]
    public void MutedRoutingKeepsTheScheduledFrameTimelineWithStereoSilence()
    {
        const int blockFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        session.SetRouting(AudioRouting.Muted);

        var transition = ProcessSines(session, startFrame: 0, blockFrames);
        var muted = ProcessSines(
            session,
            startFrame: blockFrames,
            blockFrames);

        Assert.Equal(
            blockFrames * ChannelCount,
            transition.InterleavedSamples.Length);
        Assert.Equal(blockFrames, muted.StartFrame);
        Assert.Equal(
            blockFrames * ChannelCount,
            muted.InterleavedSamples.Length);
        Assert.All(muted.InterleavedSamples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void LostMicrophoneContinuesDesktopAndEmitsTypedWarning()
    {
        const int blockFrames = 480;
        var events = new RecordingAudioSessionEventSink();
        var rediscovery = new RecordingRediscoveryScheduler();
        var session = new AudioSessionService(
            AudioRouting.Mixed,
            rediscovery,
            events);
        var desktop = GenerateStereoSine(
            440,
            startFrame: 0,
            blockFrames,
            amplitude: 0.25);

        var mixed = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            SampleRate,
            blockFrames,
            desktop,
            MicrophoneInterleavedSamples: [],
            AudioInputAvailability.Desktop));

        AssertSamples(desktop, mixed.InterleavedSamples);
        var warning = Assert.Single(events.Warnings);
        Assert.Equal(AudioSessionWarningKind.InputUnavailable, warning.Kind);
        Assert.Equal(AudioInput.Microphone, warning.Input);
        Assert.Equal(0, warning.FramePosition);
        Assert.Empty(rediscovery.Requests);
    }

    [Fact]
    public void LostDesktopSchedulesBoundedRediscoveryOnceThenUsesSilenceAndFadesRecovery()
    {
        const int blockFrames = 480;
        var events = new RecordingAudioSessionEventSink();
        var rediscovery = new RecordingRediscoveryScheduler();
        var session = new AudioSessionService(
            AudioRouting.DesktopOnly,
            rediscovery,
            events);
        ProcessSines(session, startFrame: 0, blockFrames);

        var lost = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames,
            SampleRate,
            blockFrames,
            DesktopInterleavedSamples: [],
            GenerateStereoSine(
                880,
                blockFrames,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.Microphone));
        var stillLost = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames * 2,
            SampleRate,
            blockFrames,
            DesktopInterleavedSamples: [],
            GenerateStereoSine(
                880,
                blockFrames * 2,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.Microphone));

        Assert.All(lost.InterleavedSamples, sample => Assert.Equal(0, sample));
        Assert.All(
            stillLost.InterleavedSamples,
            sample => Assert.Equal(0, sample));
        var request = Assert.Single(rediscovery.Requests);
        Assert.Equal(AudioDeviceRecoveryPolicy.ForDesktopLoss(), request);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Budget);
        Assert.Contains(events.Statuses, status =>
            status.Kind == AudioSessionStatusKind.EndpointRediscoveryScheduled &&
            status.Input == AudioInput.Desktop &&
            status.FramePosition == blockFrames);

        var recoveredDesktop = GenerateStereoSine(
            440,
            blockFrames * 3,
            blockFrames,
            amplitude: 0.25);
        var recovered = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames * 3,
            SampleRate,
            blockFrames,
            recoveredDesktop,
            GenerateStereoSine(
                880,
                blockFrames * 3,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.All));
        AssertRamp(
            recovered.InterleavedSamples,
            new float[blockFrames * ChannelCount],
            recoveredDesktop,
            gainAtFrame: frame => (double)frame / blockFrames);
        Assert.Contains(events.Statuses, status =>
            status.Kind == AudioSessionStatusKind.InputRecovered &&
            status.Input == AudioInput.Desktop &&
            status.FramePosition == blockFrames * 3);

        var steady = ProcessSines(
            session,
            startFrame: blockFrames * 4,
            blockFrames);
        AssertSamples(
            GenerateStereoSine(
                440,
                blockFrames * 4,
                blockFrames,
                amplitude: 0.25),
            steady.InterleavedSamples);
    }

    [Fact]
    public void DesktopRecoveryBeforeLossRampCompletesRestartsFadeFromSilence()
    {
        const int initialFrames = 480;
        const int lostFrames = 240;
        const int recoveryFrames = 480;
        var session = CreateSession(AudioRouting.DesktopOnly);
        ProcessSines(session, startFrame: 0, initialFrames);
        var lost = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: initialFrames,
            SampleRate,
            lostFrames,
            DesktopInterleavedSamples: [],
            GenerateStereoSine(
                880,
                initialFrames,
                lostFrames,
                amplitude: 0.25),
            AudioInputAvailability.Microphone));
        Assert.All(lost.InterleavedSamples, sample => Assert.Equal(0, sample));

        var recoveryStart = initialFrames + lostFrames;
        var desktop = GenerateStereoSine(
            440,
            recoveryStart,
            recoveryFrames,
            amplitude: 0.25);
        var recovered = session.Process(new ScheduledStereoAudioBuffer(
            recoveryStart,
            SampleRate,
            recoveryFrames,
            desktop,
            GenerateStereoSine(
                880,
                recoveryStart,
                recoveryFrames,
                amplitude: 0.25),
            AudioInputAvailability.All));

        AssertRamp(
            recovered.InterleavedSamples,
            new float[recoveryFrames * ChannelCount],
            desktop,
            gainAtFrame: frame => (double)frame / recoveryFrames);
    }

    [Fact]
    public void RediscoveryFailureWarnsAndStillReturnsScheduledSilence()
    {
        const int blockFrames = 480;
        var events = new RecordingAudioSessionEventSink();
        var session = new AudioSessionService(
            AudioRouting.DesktopOnly,
            new ThrowingRediscoveryScheduler(),
            events);

        var mixed = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            SampleRate,
            blockFrames,
            DesktopInterleavedSamples: [],
            GenerateStereoSine(
                880,
                startFrame: 0,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.Microphone));

        Assert.Equal(
            blockFrames * ChannelCount,
            mixed.InterleavedSamples.Length);
        Assert.All(mixed.InterleavedSamples, sample => Assert.Equal(0, sample));
        Assert.Contains(events.Warnings, warning =>
            warning.Kind == AudioSessionWarningKind.EndpointRediscoveryFailed &&
            warning.Input == AudioInput.Desktop);
    }

    [Fact]
    public void EventSinkFailureCannotInterruptTheAudioTimeline()
    {
        const int blockFrames = 480;
        var session = new AudioSessionService(
            AudioRouting.Mixed,
            new RecordingRediscoveryScheduler(),
            new ThrowingAudioSessionEventSink());
        var desktop = GenerateStereoSine(
            440,
            startFrame: 0,
            blockFrames,
            amplitude: 0.25);

        var first = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            SampleRate,
            blockFrames,
            desktop,
            MicrophoneInterleavedSamples: [],
            AudioInputAvailability.Desktop));
        var second = session.Process(new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames,
            SampleRate,
            blockFrames,
            GenerateStereoSine(
                440,
                blockFrames,
                blockFrames,
                amplitude: 0.25),
            GenerateStereoSine(
                880,
                blockFrames,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.All));

        AssertSamples(desktop, first.InterleavedSamples);
        Assert.Equal(blockFrames, second.StartFrame);
        Assert.Equal(
            blockFrames * ChannelCount,
            second.InterleavedSamples.Length);
    }

    [Fact]
    public void InvalidBufferDoesNotAdvanceTheSessionFrameTimeline()
    {
        const int blockFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        ProcessSines(session, startFrame: 0, blockFrames);

        var mismatch = new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames,
            SampleRate,
            blockFrames,
            DesktopInterleavedSamples:
                new float[(blockFrames * ChannelCount) - 1],
            MicrophoneInterleavedSamples:
                new float[blockFrames * ChannelCount],
            AudioInputAvailability.All);
        Assert.Throws<ArgumentException>(() => session.Process(mismatch));

        var valid = ProcessSines(
            session,
            startFrame: blockFrames,
            blockFrames);
        Assert.Equal(blockFrames, valid.StartFrame);
    }

    [Fact]
    public void DiscontinuousFramePositionFailsClosed()
    {
        const int blockFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        ProcessSines(session, startFrame: 0, blockFrames);

        var discontinuous = new ScheduledStereoAudioBuffer(
            StartFrame: blockFrames + 1,
            SampleRate,
            blockFrames,
            GenerateStereoSine(
                440,
                blockFrames + 1,
                blockFrames,
                amplitude: 0.25),
            GenerateStereoSine(
                880,
                blockFrames + 1,
                blockFrames,
                amplitude: 0.25),
            AudioInputAvailability.All);

        Assert.Throws<ArgumentException>(() => session.Process(discontinuous));
        Assert.Equal(
            blockFrames,
            ProcessSines(session, blockFrames, blockFrames).StartFrame);
    }

    [Theory]
    [InlineData(44_100, 480)]
    [InlineData(SampleRate, 4_801)]
    public void UnsupportedOrOversizedBuffersFailClosed(
        int sampleRate,
        int frameCount)
    {
        var session = CreateSession(AudioRouting.Mixed);
        var buffer = new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            sampleRate,
            frameCount,
            new float[frameCount * ChannelCount],
            new float[frameCount * ChannelCount],
            AudioInputAvailability.All);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Process(buffer));
    }

    [Fact]
    public void ContradictoryAvailabilityAndNonFiniteSamplesFailClosed()
    {
        const int blockFrames = 480;
        var session = CreateSession(AudioRouting.Mixed);
        var contradictory = new ScheduledStereoAudioBuffer(
            StartFrame: 0,
            SampleRate,
            blockFrames,
            new float[blockFrames * ChannelCount],
            new float[blockFrames * ChannelCount],
            AudioInputAvailability.Desktop);
        Assert.Throws<ArgumentException>(() => session.Process(contradictory));

        var desktop = new float[blockFrames * ChannelCount];
        desktop[20] = float.NaN;
        var nonFinite = contradictory with
        {
            DesktopInterleavedSamples = desktop,
            MicrophoneInterleavedSamples = [],
        };
        Assert.Throws<ArgumentException>(() => session.Process(nonFinite));
    }

    [Fact]
    public void ConstructionAndRoutingChangesRejectInvalidContracts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AudioSessionService(
                (AudioRouting)int.MaxValue,
                new RecordingRediscoveryScheduler(),
                new RecordingAudioSessionEventSink()));
        Assert.Throws<ArgumentNullException>(() =>
            new AudioSessionService(
                AudioRouting.Mixed,
                null!,
                new RecordingAudioSessionEventSink()));
        Assert.Throws<ArgumentNullException>(() =>
            new AudioSessionService(
                AudioRouting.Mixed,
                new RecordingRediscoveryScheduler(),
                null!));

        var session = CreateSession(AudioRouting.Mixed);
        session.SetRouting(AudioRouting.Mixed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.SetRouting((AudioRouting)int.MaxValue));

        var unavailable = session.Process(new ScheduledStereoAudioBuffer(
            0,
            SampleRate,
            1,
            [],
            [],
            AudioInputAvailability.None));
        session.SetRouting(AudioRouting.DesktopOnly);
        Assert.Equal([0f, 0f], unavailable.InterleavedSamples);
    }

    [Fact]
    public void ScheduledBufferRejectsNegativeZeroAndUnknownTimelineValues()
    {
        var session = CreateSession(AudioRouting.Mixed);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Process(new ScheduledStereoAudioBuffer(
                -1,
                SampleRate,
                1,
                [0, 0],
                [0, 0],
                AudioInputAvailability.All)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Process(new ScheduledStereoAudioBuffer(
                0,
                SampleRate,
                0,
                [],
                [],
                AudioInputAvailability.All)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Process(new ScheduledStereoAudioBuffer(
                0,
                SampleRate,
                1,
                [0, 0],
                [0, 0],
                (AudioInputAvailability)int.MaxValue)));
    }

    private static AudioSessionService CreateSession(AudioRouting routing) =>
        new(
            routing,
            new RecordingRediscoveryScheduler(),
            new RecordingAudioSessionEventSink());

    private static MixedStereoAudioBuffer ProcessSines(
        AudioSessionService session,
        long startFrame,
        int frameCount) =>
        session.Process(new ScheduledStereoAudioBuffer(
            startFrame,
            SampleRate,
            frameCount,
            GenerateStereoSine(440, startFrame, frameCount, amplitude: 0.25),
            GenerateStereoSine(880, startFrame, frameCount, amplitude: 0.25),
            AudioInputAvailability.All));

    private static float[] GenerateStereoSine(
        double frequency,
        long startFrame,
        int frameCount,
        double amplitude)
    {
        var output = new float[frameCount * ChannelCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = (float)(amplitude * Math.Sin(
                2 * Math.PI * frequency * (startFrame + frame) / SampleRate));
            output[frame * ChannelCount] = sample;
            output[(frame * ChannelCount) + 1] = sample;
        }

        return output;
    }

    private static double LeftChannelProjectionMagnitude(
        float[] interleavedSamples,
        double frequency)
    {
        var frameCount = interleavedSamples.Length / ChannelCount;
        var sine = 0d;
        var cosine = 0d;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var angle = 2 * Math.PI * frequency * frame / SampleRate;
            var sample = interleavedSamples[frame * ChannelCount];
            sine += sample * Math.Sin(angle);
            cosine += sample * Math.Cos(angle);
        }

        return 2 * Math.Sqrt((sine * sine) + (cosine * cosine)) /
               frameCount;
    }

    private static void AssertRamp(
        float[] actual,
        float[] constantContribution,
        float[] rampedContribution,
        Func<int, double> gainAtFrame)
    {
        Assert.Equal(constantContribution.Length, actual.Length);
        Assert.Equal(rampedContribution.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var expected = constantContribution[index] +
                           (rampedContribution[index] *
                            gainAtFrame(index / ChannelCount));
            Assert.InRange(Math.Abs(expected - actual[index]), 0, 0.000001);
        }
    }

    private static void AssertInterleavedStereoPairs(
        float[] samples)
    {
        Assert.Equal(0, samples.Length % ChannelCount);
        for (var index = 0; index < samples.Length; index += ChannelCount)
        {
            Assert.Equal(samples[index], samples[index + 1]);
        }
    }

    private static void AssertSamples(
        float[] expected,
        float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.InRange(
                Math.Abs(expected[index] - actual[index]),
                0,
                0.000001);
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

    private sealed class ThrowingAudioSessionEventSink : IAudioSessionEventSink
    {
        public void Publish(AudioSessionWarning warning) =>
            throw new InvalidOperationException("warning UI unavailable");

        public void Publish(AudioSessionStatus status) =>
            throw new InvalidOperationException("status UI unavailable");
    }
}
