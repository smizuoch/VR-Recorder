using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeSavedRecordingSink : ISavedRecordingSink
{
    public List<FinalizedRecording> Recordings { get; } = [];

    public Task PublishAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        Recordings.Add(recording);
        return Task.CompletedTask;
    }
}
