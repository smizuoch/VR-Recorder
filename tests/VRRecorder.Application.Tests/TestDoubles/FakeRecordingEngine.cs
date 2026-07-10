using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingEngine : IRecordingEngine
{
    public int StartCallCount { get; private set; }

    public List<string> CreatedFiles { get; } = [];

    public Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        StartCallCount++;
        CreatedFiles.Add("recording.recording.mp4");
        return Task.FromResult(new RecordingHandle("test-recording"));
    }

    public Task<RecordingResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
