using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingStopCoordinatorTests
{
    [Fact]
    public async Task DuplicateStopRequestsShareOneEngineStop()
    {
        var engine = new FakeRecordingEngine();
        var handle = new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var coordinator = new RecordingStopCoordinator(engine, handle);

        var first = coordinator.StopAsync();
        var second = coordinator.StopAsync();

        Assert.Same(first, second);
        Assert.Equal(1, engine.StopCallCount);

        var expected = new RecordingStopResult(
            new PendingRecording(
                Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "recording.mp4")),
            VideoPacketCount: 90,
            AudioPacketCount: 142);
        engine.CompleteStop(expected);

        Assert.Equal(expected, await first);
        Assert.Equal(expected, await second);
        Assert.Same(first, coordinator.StopAsync());
        Assert.Equal(1, engine.StopCallCount);
    }
}
