using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Ports;

public interface ITestRecordingPlaybackVerifier
{
    Task<TestRecordingPlaybackEvidence?> VerifyAsync(
        TimeSpan requestedDuration,
        CancellationToken cancellationToken);
}
