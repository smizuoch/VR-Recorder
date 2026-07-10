using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingStorageMonitor : IRecordingStorageMonitor
{
    public List<(RecordingHandle Handle, OutputPath OutputPath)> Requests
    {
        get;
    } = [];

    public Task RunAsync(
        RecordingHandle handle,
        OutputPath outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((handle, outputPath));
        return Task.CompletedTask;
    }
}
