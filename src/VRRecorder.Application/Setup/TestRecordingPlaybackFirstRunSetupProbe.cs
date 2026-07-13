using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class TestRecordingPlaybackFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private static readonly TimeSpan RequestedDuration =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumAcceptedDuration =
        TimeSpan.FromSeconds(4);
    private readonly ITestRecordingPlaybackVerifier _verifier;

    public TestRecordingPlaybackFirstRunSetupProbe(
        ITestRecordingPlaybackVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        _verifier = verifier;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.TestRecordingPlayback)
        {
            return false;
        }

        var evidence = await _verifier
            .VerifyAsync(RequestedDuration, cancellationToken)
            .ConfigureAwait(false);
        return evidence is not null &&
               evidence.Duration >= RequestedDuration &&
               evidence.Duration <= MaximumAcceptedDuration &&
               evidence.IsFinalized &&
               evidence.HasVideoStream &&
               evidence.HasAudioStream &&
               evidence.PlaybackStarted;
    }
}
