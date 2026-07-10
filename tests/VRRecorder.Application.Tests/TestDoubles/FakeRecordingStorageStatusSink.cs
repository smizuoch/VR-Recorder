using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingStorageStatusSink
    : IRecordingStorageStatusSink
{
    public List<RecordingStorageSnapshot> Snapshots { get; } = [];

    public Task PublishAsync(
        RecordingStorageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }
}
