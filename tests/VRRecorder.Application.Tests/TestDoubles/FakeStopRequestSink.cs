using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeStopRequestSink : IStopRequestSink
{
    public List<RecordingHandle> RequestedHandles { get; } = [];

    public Task RequestStopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        RequestedHandles.Add(handle);
        return Task.CompletedTask;
    }
}
