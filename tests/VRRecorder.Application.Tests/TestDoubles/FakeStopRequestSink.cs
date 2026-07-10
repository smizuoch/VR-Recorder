using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeStopRequestSink : IStopRequestSink
{
    public List<RecordingStopRequest> Requests { get; } = [];

    public IEnumerable<RecordingHandle> RequestedHandles =>
        Requests.Select(request => request.Handle);

    public Task RequestStopAsync(
        RecordingStopRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}
