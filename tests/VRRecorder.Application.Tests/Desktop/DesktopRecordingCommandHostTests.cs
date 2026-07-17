using VRRecorder.Application.Audio;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingCommandHostTests
{
    [Fact]
    public async Task RelaysAudioCommandsAndSameStateRuntimeRevisions()
    {
        var runtime = new ControllableDesktopRecordingRuntime();
        await using var host = new DesktopRecordingCommandHost(
            new StubDesktopRecordingRuntimeFactory(runtime));
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);
        runtime.Publish(
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(
                AudioRouting.DesktopOnly));
        var recordingRevision = host.Current.Revision;

        var updated = await host.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            CancellationToken.None);

        Assert.Equal(
            [RecordingAudioCommand.ToggleMicrophone],
            runtime.AudioCommands);
        Assert.Equal(AudioRouting.Mixed, updated.EffectiveRouting);
        Assert.Equal(RecorderState.Recording, host.Current.State);
        Assert.Equal(recordingRevision + 1, host.Current.Revision);
        Assert.Equal(updated, host.Current.AudioControlState);
    }

    [Fact]
    public async Task RelaysRuntimeStatusWithHostOwnedMonotonicRevisions()
    {
        var runtime = new ControllableDesktopRecordingRuntime();
        await using var host = new DesktopRecordingCommandHost(
            new StubDesktopRecordingRuntimeFactory(runtime));
        List<RecorderStatusSnapshot> statuses = [];
        using var subscription = host.Subscribe(statuses.Add);

        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);
        runtime.Publish(RecorderState.Arming);
        runtime.Publish(RecorderState.Countdown);
        runtime.Publish(RecorderState.Starting);
        runtime.Publish(RecorderState.Recording);
        runtime.Publish(RecorderState.SignalLost);
        runtime.Publish(RecorderState.Stopping);
        runtime.Publish(RecorderState.Ready);

        Assert.Equal(RecorderState.Ready, host.Current.State);
        Assert.Equal(
            [
                RecorderState.Booting,
                RecorderState.Ready,
                RecorderState.Arming,
                RecorderState.Countdown,
                RecorderState.Starting,
                RecorderState.Recording,
                RecorderState.SignalLost,
                RecorderState.Stopping,
                RecorderState.Ready,
            ],
            statuses.Select(status => status.State));
        Assert.Equal(
            Enumerable.Range(0, statuses.Count).Select(value => (long)value),
            statuses.Select(status => status.Revision));
    }

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
    public async Task ConcurrentToggleActivationsAreBothRoutedToRuntime()
    {
        var runtime = new ControllableDesktopRecordingRuntime(holdToggle: true);
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        await using var host = new DesktopRecordingCommandHost(factory);
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);

        var first = host.ToggleAsync(CancellationToken.None);
        await runtime.WaitUntilToggleRequestedAsync();
        var second = host.ToggleAsync(CancellationToken.None);

        Assert.Equal(2, runtime.ToggleCallCount);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        runtime.CompleteToggle();
        await Task.WhenAll(first, second);

        Assert.Equal(2, runtime.ToggleCallCount);
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
        List<RecorderStatusSnapshot> statuses = [];
        using var subscription = host.Subscribe(statuses.Add);

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
        Assert.Equal(
            [RecorderState.Booting, RecorderState.Faulted],
            statuses.Select(status => status.State));
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
        Assert.Equal(
            [RecordingStopReason.ApplicationShutdown],
            runtime.ShutdownReasons);
    }

    [Fact]
    public async Task ComplianceFaultDuringInitializationCanNeverPublishReady()
    {
        var factory = new ControllableDesktopRecordingRuntimeFactory();
        var runtime = new DisposalTrackingDesktopRecordingRuntime();
        await using var host = new DesktopRecordingCommandHost(factory);
        List<RecorderStatusSnapshot> statuses = [];
        using var subscription = host.Subscribe(statuses.Add);
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
        Assert.Equal(
            [RecordingStopReason.ComplianceFault],
            runtime.ShutdownReasons);
        runtime.Publish(RecorderState.Ready);
        runtime.Publish(RecorderState.Faulted);
        Assert.Equal(
            [RecorderState.Booting, RecorderState.ComplianceFault],
            statuses.Select(status => status.State));
        Assert.Equal(RecorderState.ComplianceFault, host.Current.State);
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
        Assert.Equal(
            [RecordingStopReason.ComplianceFault],
            runtime.ShutdownReasons);

        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.Equal(1, runtime.DisposeCallCount);
        Assert.Equal(
            [RecordingStopReason.ComplianceFault],
            runtime.ShutdownReasons);
    }

    [Fact]
    public async Task DisposeWinsLaterComplianceFaultRaceWithOneTypedShutdown()
    {
        var runtime = new ControllableDesktopRecordingRuntime(
            holdShutdown: true);
        var factory = new StubDesktopRecordingRuntimeFactory(runtime);
        var host = new DesktopRecordingCommandHost(factory);
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);

        var disposal = host.DisposeAsync().AsTask();
        var complianceFault = ((IComplianceFaultSink)host)
            .EnterComplianceFaultAsync()
            .AsTask();
        await runtime.WaitUntilShutdownRequestedAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(disposal, complianceFault);
        Assert.Equal(
            [RecordingStopReason.ApplicationShutdown],
            runtime.ShutdownReasons);
        Assert.Equal(1, runtime.DisposeCallCount);

        runtime.CompleteShutdown();
        await Task.WhenAll(disposal, complianceFault);

        Assert.Equal(1, runtime.DisposeCallCount);
        Assert.Equal(
            [RecordingStopReason.ApplicationShutdown],
            runtime.ShutdownReasons);
    }

    [Fact]
    public async Task PublicCommandsValidateStateAndSupportCancelableWaits()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DesktopRecordingCommandHost(null!));
        var runtime = new ControllableDesktopRecordingRuntime();
        var host = new DesktopRecordingCommandHost(
            new StubDesktopRecordingRuntimeFactory(runtime));
        Assert.Null(host.CurrentAudioControlState);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.ActivateAsync(
                new RecorderStartupResult(RecorderState.Booting, []),
                CancellationToken.None));
        await Assert.ThrowsAsync<DesktopRecordingUnavailableException>(() =>
            host.ExecuteAudioCommandAsync(
                RecordingAudioCommand.ToggleMicrophone,
                CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        var activation = await host.ActivateAsync(
            ReadyStartup(),
            cancellation.Token);
        Assert.Equal(DesktopRecordingHostState.Ready, activation.State);
        runtime.Publish(
            RecorderState.Recording,
            RecordingAudioControlState.FromRouting(AudioRouting.Mixed));
        await host.ToggleAsync(cancellation.Token);
        var audio = await host.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            cancellation.Token);
        Assert.Equal(AudioRouting.DesktopOnly, audio.EffectiveRouting);

        await host.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            host.ActivateAsync(ReadyStartup(), CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            host.ToggleAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            host.ExecuteAudioCommandAsync(
                RecordingAudioCommand.ToggleMicrophone,
                CancellationToken.None));
    }

    [Fact]
    public async Task ComplianceActivationSupportsCancelableWaitAndIsIdempotent()
    {
        var host = new DesktopRecordingCommandHost(
            new StubDesktopRecordingRuntimeFactory(
                new ControllableDesktopRecordingRuntime()));
        using var cancellation = new CancellationTokenSource();
        var startup = new RecorderStartupResult(
            RecorderState.ComplianceFault,
            [new LegalBundleIssue("fault", "catalog")]);

        var first = await host.ActivateAsync(startup, cancellation.Token);
        await ((IComplianceFaultSink)host).EnterComplianceFaultAsync();
        var second = await host.ActivateAsync(
            ReadyStartup(),
            cancellation.Token);

        Assert.Equal(DesktopRecordingHostState.ComplianceFault, first.State);
        Assert.Equal(DesktopRecordingHostState.ComplianceFault, second.State);
        await host.DisposeAsync();
        var disposed = await host.ActivateAsync(startup, cancellation.Token);
        Assert.Equal(DesktopRecordingHostState.Disposed, disposed.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedOrNullRuntimeInitializationUsesGenericFailureCode(
        bool returnsNull)
    {
        var factory = returnsNull
            ? new StubDesktopRecordingRuntimeFactory(
                (IDesktopRecordingRuntime)null!)
            : new StubDesktopRecordingRuntimeFactory(
                new InvalidOperationException("unexpected failure"));
        await using var host = new DesktopRecordingCommandHost(factory);

        var activation = await host.ActivateAsync(
            ReadyStartup(),
            CancellationToken.None);

        Assert.Equal(
            DesktopRecordingHostState.InitializationFailed,
            activation.State);
        Assert.Equal("RECORDING_INITIALIZATION_FAILED", activation.Failure?.Code);
    }

    [Fact]
    public async Task DuplicateAndTerminalRuntimeStatusesCannotAdvanceRevision()
    {
        var runtime = new ControllableDesktopRecordingRuntime();
        await using var host = new DesktopRecordingCommandHost(
            new StubDesktopRecordingRuntimeFactory(runtime));
        await host.ActivateAsync(ReadyStartup(), CancellationToken.None);
        var readyRevision = host.Current.Revision;

        runtime.Publish(RecorderState.Ready);
        Assert.Equal(readyRevision, host.Current.Revision);
        runtime.Publish(RecorderState.Faulted);
        var terminalRevision = host.Current.Revision;
        runtime.Publish(RecorderState.Recording);

        Assert.Equal(RecorderState.Faulted, host.Current.State);
        Assert.Equal(terminalRevision, host.Current.Revision);
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
        : IDesktopRecordingRuntime,
          IRecorderStatusSource
    {
        private readonly TaskCompletionSource _toggleRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _toggleCompletion;
        private readonly TaskCompletionSource _shutdownRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _shutdownCompletion;
        private readonly RecorderStatusHub _statuses = new(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        private long _statusRevision;

        public ControllableDesktopRecordingRuntime(
            bool holdToggle = false,
            bool holdShutdown = false)
        {
            _toggleCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!holdToggle)
            {
                _toggleCompletion.SetResult();
            }

            _shutdownCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!holdShutdown)
            {
                _shutdownCompletion.SetResult();
            }
        }

        public int ToggleCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public List<RecordingAudioCommand> AudioCommands { get; } = [];

        public List<RecordingStopReason> ShutdownReasons { get; } = [];

        public RecorderStatusSnapshot Current => _statuses.Current;

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            _statuses.Subscribe(subscriber);

        public void Publish(
            RecorderState state,
            RecordingAudioControlState? audioControlState = null) =>
            _statuses.TryPublish(RecorderStatusSnapshot.Create(
                ++_statusRevision,
                state,
                audioControlState));

        public Task<RecordingAudioControlState> ExecuteAudioCommandAsync(
            RecordingAudioCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioCommands.Add(command);
            var updated = (Current.AudioControlState ??
                           throw new InvalidOperationException(
                               "No audio state is active."))
                .Apply(command);
            Publish(Current.State, updated);
            return Task.FromResult(updated);
        }

        public Task ToggleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToggleCallCount++;
            _toggleRequested.TrySetResult();
            return _toggleCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task ShutdownAsync(RecordingStopReason reason)
        {
            ShutdownReasons.Add(reason);
            DisposeCallCount++;
            _shutdownRequested.TrySetResult();
            return _shutdownCompletion.Task;
        }

        public Task WaitUntilToggleRequestedAsync() => _toggleRequested.Task;

        public Task WaitUntilShutdownRequestedAsync() =>
            _shutdownRequested.Task;

        public void CompleteToggle() => _toggleCompletion.TrySetResult();

        public void CompleteShutdown() => _shutdownCompletion.TrySetResult();

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
        : IDesktopRecordingRuntime,
          IRecorderStatusSource
    {
        private readonly RecorderStatusHub _statuses = new(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        private long _statusRevision;

        public int DisposeCallCount { get; private set; }

        public List<RecordingStopReason> ShutdownReasons { get; } = [];

        public RecorderStatusSnapshot Current => _statuses.Current;

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            _statuses.Subscribe(subscriber);

        public void Publish(RecorderState state) =>
            _statuses.TryPublish(RecorderStatusSnapshot.Create(
                ++_statusRevision,
                state));

        public Task ToggleAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShutdownAsync(RecordingStopReason reason)
        {
            ShutdownReasons.Add(reason);
            DisposeCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
