using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class TestRecordingPlaybackFirstRunSetupProbeTests
{
    [Fact]
    public async Task FinalizedThreeSecondAvRecordingWithPlaybackVerifies()
    {
        var verifier = new StubVerifier(new TestRecordingPlaybackEvidence(
            TimeSpan.FromMilliseconds(3200),
            IsFinalized: true,
            HasVideoStream: true,
            HasAudioStream: true,
            PlaybackStarted: true));
        var probe = new TestRecordingPlaybackFirstRunSetupProbe(verifier);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.TestRecordingPlayback,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(TimeSpan.FromSeconds(3), verifier.RequestedDuration);
    }

    [Theory]
    [InlineData(2999, true, true, true, true)]
    [InlineData(4001, true, true, true, true)]
    [InlineData(3200, false, true, true, true)]
    [InlineData(3200, true, false, true, true)]
    [InlineData(3200, true, true, false, true)]
    [InlineData(3200, true, true, true, false)]
    public async Task IncompleteDurationMediaFinalizationOrPlaybackDoesNotVerify(
        int durationMilliseconds,
        bool finalized,
        bool video,
        bool audio,
        bool playback)
    {
        var probe = new TestRecordingPlaybackFirstRunSetupProbe(
            new StubVerifier(new TestRecordingPlaybackEvidence(
                TimeSpan.FromMilliseconds(durationMilliseconds),
                finalized,
                video,
                audio,
                playback)));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.TestRecordingPlayback,
            CancellationToken.None));
    }

    [Fact]
    public async Task OtherStepDoesNotStartTestRecording()
    {
        var verifier = new StubVerifier(evidence: null);
        var probe = new TestRecordingPlaybackFirstRunSetupProbe(verifier);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.LegalBundleVerification,
            CancellationToken.None));
        Assert.Null(verifier.RequestedDuration);
    }

    private sealed class StubVerifier(TestRecordingPlaybackEvidence? evidence)
        : ITestRecordingPlaybackVerifier
    {
        public TimeSpan? RequestedDuration { get; private set; }

        public Task<TestRecordingPlaybackEvidence?> VerifyAsync(
            TimeSpan requestedDuration,
            CancellationToken cancellationToken)
        {
            RequestedDuration = requestedDuration;
            return Task.FromResult(evidence);
        }
    }
}
