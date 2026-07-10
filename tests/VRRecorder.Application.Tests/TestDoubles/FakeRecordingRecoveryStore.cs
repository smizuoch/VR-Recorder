using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingRecoveryStore : IRecordingRecoveryStore
{
    public List<FinalizedRecording> Recordings { get; } = [];

    public Task QuarantineAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        Recordings.Add(recording);
        return Task.CompletedTask;
    }
}
