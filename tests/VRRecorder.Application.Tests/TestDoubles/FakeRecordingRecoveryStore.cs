using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingRecoveryStore : IRecordingRecoveryStore
{
    public List<RecoverableRecording> Recordings { get; } = [];

    public QuarantinedRecording QuarantinedRecording { get; } =
        new("recovery/recording.mp4");

    public Task<QuarantinedRecording> QuarantineAsync(
        RecoverableRecording recording,
        CancellationToken cancellationToken)
    {
        Recordings.Add(recording);
        return Task.FromResult(QuarantinedRecording);
    }
}
