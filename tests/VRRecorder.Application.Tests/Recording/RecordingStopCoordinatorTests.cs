using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingStopCoordinatorTests
{
    [Fact]
    public async Task DuplicateStopRequestsShareOneEngineStop()
    {
        var engine = new FakeRecordingEngine();
        var handle = new RecordingHandle("session-001");
        var coordinator = new RecordingStopCoordinator(engine, handle);

        var first = coordinator.StopAsync(CancellationToken.None);
        var second = coordinator.StopAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, engine.StopCallCount);

        var expected = new RecordingResult("recording.mp4");
        engine.CompleteStop(expected);

        Assert.Equal(expected, await first);
        Assert.Equal(expected, await second);
    }
}
