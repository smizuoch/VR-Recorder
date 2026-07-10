using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
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

    [Fact]
    public async Task RuntimeReturnedAfterDisposalIsDisposedExactlyOnce()
    {
        var factory = new ControllableDesktopRecordingRuntimeFactory();
        var runtime = new DisposalTrackingDesktopRecordingRuntime();
        var host = new DesktopRecordingCommandHost(factory);
        var activation = host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);
        await factory.WaitUntilInitializeRequestedAsync();

        var disposal = host.DisposeAsync().AsTask();
        factory.Complete(runtime);

        await disposal;
        var result = await activation;
        await host.DisposeAsync();

        Assert.Equal(DesktopRecordingHostState.Disposed, result.State);
        Assert.Equal(1, runtime.DisposeCallCount);
    }

    [Fact]
    public async Task ComplianceFaultDuringInitializationCanNeverPublishReady()
    {
        var factory = new ControllableDesktopRecordingRuntimeFactory();
        var runtime = new DisposalTrackingDesktopRecordingRuntime();
        await using var host = new DesktopRecordingCommandHost(factory);
        var activation = host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);
        await factory.WaitUntilInitializeRequestedAsync();

        var complianceFault = ((IComplianceFaultSink)host)
            .EnterComplianceFaultAsync()
            .AsTask();
        factory.Complete(runtime);

        var racedActivation = await activation;
        await complianceFault;
        var cachedActivation = await host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<
            DesktopRecordingUnavailableException>(() =>
            host.ToggleAsync(CancellationToken.None));

        Assert.Equal(
            DesktopRecordingHostState.ComplianceFault,
            racedActivation.State);
        Assert.Equal(
            DesktopRecordingHostState.ComplianceFault,
            cachedActivation.State);
        Assert.Equal(
            DesktopRecordingHostState.ComplianceFault,
            exception.State);
        Assert.Equal(DesktopRecordingHostState.ComplianceFault, host.State);
        Assert.Equal(1, runtime.DisposeCallCount);
    }

    [Fact]
    public async Task ComplianceFaultCancelsToggleAndDisposesRuntimeExactlyOnce()
    {
        var runtime = new ControllableDesktopRecordingRuntime(holdToggle: true);
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        var host = new DesktopRecordingCommandHost(factory);
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);
        var toggle = host.ToggleAsync(CancellationToken.None);
        await runtime.WaitUntilToggleRequestedAsync();

        await ((IComplianceFaultSink)host).EnterComplianceFaultAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => toggle);
        Assert.Equal(DesktopRecordingHostState.ComplianceFault, host.State);
        Assert.Equal(1, runtime.DisposeCallCount);

        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.Equal(1, runtime.DisposeCallCount);
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

        public int DisposeCallCount { get; private set; }

        public Task ToggleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToggleCallCount++;
            _toggleRequested.TrySetResult();
            return _toggleCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilToggleRequestedAsync() => _toggleRequested.Task;

        public void CompleteToggle() => _toggleCompletion.TrySetResult();

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControllableDesktopRecordingRuntimeFactory
        : IDesktopRecordingRuntimeFactory
    {
        private readonly TaskCompletionSource _initializeRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IDesktopRecordingRuntime>
            _runtime = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IDesktopRecordingRuntime> InitializeAsync(
            CancellationToken cancellationToken)
        {
            _initializeRequested.TrySetResult();
            return _runtime.Task;
        }

        public Task WaitUntilInitializeRequestedAsync() =>
            _initializeRequested.Task;

        public void Complete(IDesktopRecordingRuntime runtime) =>
            _runtime.TrySetResult(runtime);
    }

    private sealed class DisposalTrackingDesktopRecordingRuntime
        : IDesktopRecordingRuntime
    {
        public int DisposeCallCount { get; private set; }

        public Task ToggleAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
