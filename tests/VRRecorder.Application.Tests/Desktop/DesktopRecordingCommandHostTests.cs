using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingCommandHostTests
{
    [Fact]
    public async Task ReadyStartupInitializesRuntimeAndRoutesToggleOnce()
    {
        var runtime = new ControllableDesktopRecordingRuntime();
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        await using var host = new DesktopRecordingCommandHost(factory);

        var activation = await host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);
        await host.ToggleAsync(CancellationToken.None);

        Assert.Equal(DesktopRecordingHostState.Ready, activation.State);
        Assert.Null(activation.Failure);
        Assert.Equal(1, factory.InitializeCallCount);
        Assert.Equal(1, runtime.ToggleCallCount);
    }

    [Fact]
    public async Task ConcurrentToggleActivationsJoinOneInFlightOperation()
    {
        var runtime = new ControllableDesktopRecordingRuntime(holdToggle: true);
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        await using var host = new DesktopRecordingCommandHost(factory);
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);

        var first = host.ToggleAsync(CancellationToken.None);
        await runtime.WaitUntilToggleRequestedAsync();
        var second = host.ToggleAsync(CancellationToken.None);

        Assert.Equal(1, runtime.ToggleCallCount);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        runtime.CompleteToggle();
        await Task.WhenAll(first, second);

        Assert.Equal(1, runtime.ToggleCallCount);
    }

    [Fact]
    public async Task ComplianceFaultDoesNotInitializeOrAcceptToggle()
    {
        var runtime = new ControllableDesktopRecordingRuntime();
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        await using var host = new DesktopRecordingCommandHost(factory);
        RecorderStartupResult startup = new(
            RecorderState.ComplianceFault,
            [new LegalBundleIssue("LEGAL_BUNDLE_HASH_MISMATCH", "catalog")]);

        var activation = await host.ActivateAsync(
            startup,
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<
            DesktopRecordingUnavailableException>(() =>
            host.ToggleAsync(CancellationToken.None));

        Assert.Equal(
            DesktopRecordingHostState.ComplianceFault,
            activation.State);
        Assert.Equal(activation.State, exception.State);
        Assert.Equal(0, factory.InitializeCallCount);
        Assert.Equal(0, runtime.ToggleCallCount);
    }

    [Fact]
    public async Task InitializationFailureIsSurfacedAndFailsClosed()
    {
        var failure = new DesktopRecordingInitializationException(
            "NATIVE_MEDIA_UNAVAILABLE",
            "The native recording service is unavailable.");
        var factory = new StubDesktopRecordingRuntimeFactory(failure);
        await using var host = new DesktopRecordingCommandHost(factory);

        var activation = await host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<
            DesktopRecordingUnavailableException>(() =>
            host.ToggleAsync(CancellationToken.None));

        Assert.Equal(
            DesktopRecordingHostState.InitializationFailed,
            activation.State);
        Assert.Equal("NATIVE_MEDIA_UNAVAILABLE", activation.Failure?.Code);
        Assert.Equal(failure.Message, activation.Failure?.Message);
        Assert.Equal(activation.State, exception.State);
        Assert.Equal(1, factory.InitializeCallCount);
    }

    private static RecorderStartupResult ReadyStartup() =>
        new(RecorderState.Ready, []);

    private sealed class StubDesktopRecordingRuntimeFactory
        : IDesktopRecordingRuntimeFactory
    {
        private readonly IDesktopRecordingRuntime? _runtime;
        private readonly Exception? _failure;

        public StubDesktopRecordingRuntimeFactory(
            IDesktopRecordingRuntime runtime)
        {
            _runtime = runtime;
        }

        public StubDesktopRecordingRuntimeFactory(Exception failure)
        {
            _failure = failure;
        }

        public int InitializeCallCount { get; private set; }

        public Task<IDesktopRecordingRuntime> InitializeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InitializeCallCount++;
            return _failure is null
                ? Task.FromResult(_runtime!)
                : Task.FromException<IDesktopRecordingRuntime>(_failure);
        }
    }

    private sealed class ControllableDesktopRecordingRuntime
        : IDesktopRecordingRuntime
    {
        private readonly TaskCompletionSource _toggleRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _toggleCompletion;

        public ControllableDesktopRecordingRuntime(bool holdToggle = false)
        {
            _toggleCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!holdToggle)
            {
                _toggleCompletion.SetResult();
            }
        }

        public int ToggleCallCount { get; private set; }

        public Task ToggleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToggleCallCount++;
            _toggleRequested.TrySetResult();
            return _toggleCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilToggleRequestedAsync() => _toggleRequested.Task;

        public void CompleteToggle() => _toggleCompletion.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
