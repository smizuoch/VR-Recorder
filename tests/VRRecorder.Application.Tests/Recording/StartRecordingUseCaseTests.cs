using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Recording;

public sealed class StartRecordingUseCaseTests
{
    [Fact]
    public async Task ExecuteDoesNotStartEngineBeforeStableSignal()
    {
        var signal = new ControllableVideoSignalGateway();
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(signal, engine);
        using var cancellation = new CancellationTokenSource();

        var execution = useCase.ExecuteAsync(
            new StartRecordingCommand(),
            cancellation.Token);
        await signal.WaitUntilRequestedAsync();

        Assert.Equal(0, engine.StartCallCount);
        Assert.Empty(engine.CreatedFiles);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }
}
