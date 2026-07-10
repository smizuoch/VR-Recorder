using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingRuntimeTests
{
    [Fact]
    public async Task RelaysLifecycleAndOwnsStoppingProjectionWithOneRevisionOrder()
    {
        var handle = Handle("session-live-status");
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-live-status"));
        var lifecycle = new StatusRecordingLifecycle(handle);
        var stops = new PassiveStopRequestSink(lifecycle);
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);
        List<RecorderStatusSnapshot> statuses = [];
        using var subscription = runtime.Subscribe(statuses.Add);

        await runtime.ToggleAsync(CancellationToken.None);
        var stopping = runtime.ToggleAsync(CancellationToken.None);
        await stops.WaitUntilRequestedAsync();

        Assert.Equal(RecorderState.Stopping, runtime.Current.State);
        Assert.Equal(RecorderState.Stopping, statuses[^1].State);
        stops.Complete();
        await stopping;

        Assert.Equal(
            [
                RecorderState.Ready,
                RecorderState.Arming,
                RecorderState.Recording,
                RecorderState.Stopping,
                RecorderState.Ready,
            ],
            statuses.Select(status => status.State));
        Assert.Equal(
            Enumerable.Range(0, statuses.Count).Select(value => (long)value),
            statuses.Select(status => status.Revision));
        Assert.All(statuses, status => Assert.Equal(
            RecorderStatusSnapshot.Create(status.Revision, status.State),
            status));
    }

    [Fact]
    public async Task FirstToggleStartsAndSecondToggleStopsSameHandleThroughFinalization()
    {
        var handle = Handle("session-001");
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-selected"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(handle));
        var stops = new ControllableStopRequestSink(lifecycle);
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        await runtime.ToggleAsync(CancellationToken.None);
        var stopping = runtime.ToggleAsync(CancellationToken.None);
        await stops.WaitUntilRequestedAsync();

        Assert.Equal(1, requests.GetCallCount);
        Assert.Equal("vrc-selected", lifecycle.StartRequests.Single().ServiceId);
        Assert.Equal(handle, stops.Requests.Single().Request.Handle);
        Assert.Equal(
            RecordingStopReason.UserRequested,
            stops.Requests.Single().Request.Reason);
        Assert.Equal(CancellationToken.None, stops.Requests.Single().Token);
        Assert.False(stopping.IsCompleted);

        stops.Complete();
        await stopping;

        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Single(stops.Requests);
    }

    [Fact]
    public async Task NoSignalReloadsRequestAndRetriesWithoutStaleStop()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-first"));
        requests.EnqueueCompleted(Request("vrc-retry"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(NoSignal());
        lifecycle.EnqueueCompleted(Started(Handle("session-retry")));
        var stops = new ControllableStopRequestSink(lifecycle)
        {
            CompleteImmediately = true,
        };
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        await runtime.ToggleAsync(CancellationToken.None);

        Assert.Equal(RecorderState.NoSignal, lifecycle.State);
        Assert.Empty(stops.Requests);

        await runtime.ToggleAsync(CancellationToken.None);

        Assert.Equal(2, requests.GetCallCount);
        Assert.Equal(2, lifecycle.StartRequests.Count);
        Assert.Equal(
            ["vrc-first", "vrc-retry"],
            lifecycle.StartRequests.Select(request => request.ServiceId));
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.Empty(stops.Requests);
    }

    [Fact]
    public async Task CompletedExternalStopDoesNotSendStaleStopBeforeNextStart()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-first"));
        requests.EnqueueCompleted(Request("vrc-second"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-completed")));
        lifecycle.EnqueueCompleted(Started(Handle("session-next")));
        var stops = new ControllableStopRequestSink(lifecycle)
        {
            CompleteImmediately = true,
        };
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);
        await runtime.ToggleAsync(CancellationToken.None);

        lifecycle.SetState(RecorderState.Ready);
        await runtime.ToggleAsync(CancellationToken.None);

        Assert.Equal(2, lifecycle.StartRequests.Count);
        Assert.Equal("vrc-second", lifecycle.StartRequests[1].ServiceId);
        Assert.Empty(stops.Requests);
    }

    [Fact]
    public async Task StartedResultWithNoSignalLifecycleFailsClosed()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-inconsistent"));
        var lifecycle = new ControllableRecordingLifecycle
        {
            NextStartStateOverride = RecorderState.NoSignal,
        };
        lifecycle.EnqueueCompleted(Started(Handle("session-inconsistent")));
        var stops = new ControllableStopRequestSink(lifecycle);
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        var toggling = runtime.ToggleAsync(CancellationToken.None);
        await stops.WaitUntilRequestedAsync();

        Assert.Equal(
            "session-inconsistent",
            stops.Requests.Single().Request.Handle.Id);
        Assert.Equal(
            RecordingStopReason.InvariantViolation,
            stops.Requests.Single().Request.Reason);
        Assert.Equal(CancellationToken.None, stops.Requests.Single().Token);
        Assert.False(toggling.IsCompleted);

        stops.Complete();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            toggling);

        Assert.Contains("inconsistent", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(stops.Requests);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
    }

    [Fact]
    public async Task ConcurrentTransportDuplicateTogglesJoinOneStartOperation()
    {
        var requests = new ControllableStartRequestSource();
        var request = requests.EnqueuePending();
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-concurrent")));
        var stops = new ControllableStopRequestSink(lifecycle)
        {
            CompleteImmediately = true,
        };
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        var first = runtime.ToggleAsync(CancellationToken.None);
        await requests.WaitUntilRequestedAsync();
        var second = runtime.ToggleAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, requests.GetCallCount);
        Assert.False(first.IsCompleted);

        request.SetResult(Request("vrc-concurrent"));
        await Task.WhenAll(first, second);

        Assert.Single(lifecycle.StartRequests);
    }

    [Theory]
    [InlineData(RecorderState.Arming)]
    [InlineData(RecorderState.Countdown)]
    public async Task SecondToggleDuringCancelableStartPhaseCancelsSharedOperation(
        RecorderState cancelableState)
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-cancel"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle);
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        var first = runtime.ToggleAsync(CancellationToken.None);
        await lifecycle.WaitUntilStartRequestedAsync();
        lifecycle.SetState(cancelableState);

        var second = runtime.ToggleAsync(CancellationToken.None);

        Assert.Same(first, second);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Single(lifecycle.StartRequests);
        Assert.Empty(stops.Requests);
    }

    [Fact]
    public async Task CallerCancellationDuringArmingRemainsCanceled()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-external-cancel"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle);
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);
        using var callerCancellation = new CancellationTokenSource();
        var starting = runtime.ToggleAsync(callerCancellation.Token);
        await lifecycle.WaitUntilStartRequestedAsync();

        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Empty(stops.Requests);
    }

    [Fact]
    public async Task SecondToggleDuringStartingJoinsWithoutCanceling()
    {
        var handle = Handle("session-starting");
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-starting"));
        var lifecycle = new ControllableRecordingLifecycle();
        var start = lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle)
        {
            CompleteImmediately = true,
        };
        await using var runtime = new DesktopRecordingRuntime(
            requests,
            lifecycle,
            stops);

        var first = runtime.ToggleAsync(CancellationToken.None);
        await lifecycle.WaitUntilStartRequestedAsync();
        lifecycle.SetState(RecorderState.Starting);

        var second = runtime.ToggleAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        start.SetResult(Started(handle));
        await Task.WhenAll(first, second);
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.Single(lifecycle.StartRequests);
        Assert.Empty(stops.Requests);
    }

    [Fact]
    public async Task DisposeAfterArmingCancelConvergesWithoutStopRequest()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-cancel-dispose"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);

        var first = runtime.ToggleAsync(CancellationToken.None);
        await lifecycle.WaitUntilStartRequestedAsync();
        var second = runtime.ToggleAsync(CancellationToken.None);

        Assert.Same(first, second);
        await Task.WhenAll(first, second);
        var disposal = runtime.DisposeAsync().AsTask();
        await disposal;
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Empty(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeStopsActiveSessionExactlyOnceAndIsIdempotent()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-dispose"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-dispose")));
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        await runtime.ToggleAsync(CancellationToken.None);

        var first = runtime.DisposeAsync().AsTask();
        var second = runtime.DisposeAsync().AsTask();
        await stops.WaitUntilRequestedAsync();

        Assert.Same(first, second);
        Assert.Single(stops.Requests);
        Assert.Equal(
            RecordingStopReason.ApplicationShutdown,
            stops.Requests.Single().Request.Reason);
        Assert.Equal(CancellationToken.None, stops.Requests.Single().Token);
        Assert.False(first.IsCompleted);

        stops.Complete();
        await Task.WhenAll(first, second);

        Assert.Equal(1, lifecycle.DisposeCallCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            runtime.ToggleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ComplianceFaultStopsActiveSessionWithTypedReasonAndJoinsDispose()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-compliance-fault"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-compliance-fault")));
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        await runtime.ToggleAsync(CancellationToken.None);

        var complianceShutdown = runtime.ShutdownAsync(
            RecordingStopReason.ComplianceFault);
        var disposal = runtime.DisposeAsync().AsTask();
        await stops.WaitUntilRequestedAsync();

        Assert.Same(complianceShutdown, disposal);
        Assert.Single(stops.Requests);
        Assert.Equal(
            RecordingStopReason.ComplianceFault,
            stops.Requests.Single().Request.Reason);
        Assert.Equal(CancellationToken.None, stops.Requests.Single().Token);

        stops.Complete();
        await Task.WhenAll(complianceShutdown, disposal);

        Assert.Single(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeWinsConcurrentLaterComplianceShutdownExactlyOnce()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-dispose-race"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-dispose-race")));
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        await runtime.ToggleAsync(CancellationToken.None);

        var disposal = runtime.DisposeAsync().AsTask();
        var laterComplianceShutdown = runtime.ShutdownAsync(
            RecordingStopReason.ComplianceFault);
        await stops.WaitUntilRequestedAsync();

        Assert.Same(disposal, laterComplianceShutdown);
        Assert.Single(stops.Requests);
        Assert.Equal(
            RecordingStopReason.ApplicationShutdown,
            stops.Requests.Single().Request.Reason);

        stops.Complete();
        await Task.WhenAll(disposal, laterComplianceShutdown);

        Assert.Single(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Theory]
    [InlineData(RecorderState.Arming)]
    [InlineData(RecorderState.Countdown)]
    public async Task ComplianceFaultCancelsStartPhaseWithoutCreatingStopRequest(
        RecorderState startPhase)
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-compliance-start"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        var starting = runtime.ToggleAsync(CancellationToken.None);
        await lifecycle.WaitUntilStartRequestedAsync();
        lifecycle.SetState(startPhase);

        var shutdown = runtime.ShutdownAsync(
            RecordingStopReason.ComplianceFault);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        await shutdown;
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Empty(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task ShutdownRejectsNonTerminalStopReasonWithoutTakingOwnership()
    {
        var requests = new ControllableStartRequestSource();
        var lifecycle = new ControllableRecordingLifecycle();
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runtime.ShutdownAsync(RecordingStopReason.UserRequested));
        await runtime.DisposeAsync();

        Assert.Empty(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeJoinsInflightStopWithoutSecondStopRequest()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-stopping"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-stopping")));
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        await runtime.ToggleAsync(CancellationToken.None);
        var stopping = runtime.ToggleAsync(CancellationToken.None);
        await stops.WaitUntilRequestedAsync();

        var disposal = runtime.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);

        Assert.Single(stops.Requests);
        Assert.False(disposal.IsCompleted);

        stops.Complete();
        await disposal;

        Assert.Single(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task FailedStopIsReusedByDisposeWithoutSecondStopRequest()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-stop-failure"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueueCompleted(Started(Handle("session-stop-failure")));
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        await runtime.ToggleAsync(CancellationToken.None);
        var stopping = runtime.ToggleAsync(CancellationToken.None);
        await stops.WaitUntilRequestedAsync();
        var failure = new IOException("finalization failed");

        stops.Fail(failure);

        Assert.Same(
            failure,
            await Assert.ThrowsAsync<IOException>(() => stopping));
        Assert.Same(
            failure,
            await Assert.ThrowsAsync<IOException>(async () =>
                await runtime.DisposeAsync()));
        Assert.Single(stops.Requests);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeCancelsAndJoinsInflightStartWithoutStopRequest()
    {
        var requests = new ControllableStartRequestSource();
        requests.EnqueueCompleted(Request("vrc-arming"));
        var lifecycle = new ControllableRecordingLifecycle();
        lifecycle.EnqueuePending();
        var stops = new ControllableStopRequestSink(lifecycle);
        var runtime = new DesktopRecordingRuntime(requests, lifecycle, stops);
        var starting = runtime.ToggleAsync(CancellationToken.None);
        await lifecycle.WaitUntilStartRequestedAsync();

        var disposal = runtime.DisposeAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        await disposal;
        Assert.Empty(stops.Requests);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(1, lifecycle.DisposeCallCount);
    }

    private static DesktopRecordingStartRequest Request(string serviceId) =>
        new(serviceId, Command());

    private static StartRecordingCommand Command() =>
        new(
            SelfTimer.FromSeconds(0),
            RecordingDuration.Infinite,
            new OutputPath(Path.Combine(Path.GetTempPath(), "vr-recorder-runtime")),
            new FrameRate(30));

    private static RecordingHandle Handle(string id) =>
        new(id, MonotonicTimestamp.FromElapsed(TimeSpan.Zero));

    private static RecordingLifecycleStartResult Started(RecordingHandle handle) =>
        Result(
            RecorderState.Recording,
            new StartRecordingResult.Started(
                handle,
                Task.CompletedTask,
                Task.CompletedTask));

    private static RecordingLifecycleStartResult NoSignal() =>
        Result(RecorderState.NoSignal, new StartRecordingResult.NoSignal());

    private static RecordingLifecycleStartResult Result(
        RecorderState state,
        StartRecordingResult recording) =>
        new(
            state,
            new VrChatCameraConnectionResolution.NotFound(),
            recording);

    private sealed class ControllableStartRequestSource
        : IDesktopRecordingStartRequestSource
    {
        private readonly Queue<TaskCompletionSource<DesktopRecordingStartRequest>>
            _requests = [];
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int GetCallCount { get; private set; }

        public TaskCompletionSource<DesktopRecordingStartRequest> EnqueuePending()
        {
            var completion = new TaskCompletionSource<DesktopRecordingStartRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Enqueue(completion);
            return completion;
        }

        public void EnqueueCompleted(DesktopRecordingStartRequest request) =>
            EnqueuePending().SetResult(request);

        public Task<DesktopRecordingStartRequest> GetAsync(
            CancellationToken cancellationToken)
        {
            GetCallCount++;
            _requested.TrySetResult();
            return _requests.Dequeue().Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;
    }

    private sealed class StatusRecordingLifecycle(
        RecordingHandle handle)
        : IRecordingLifecycleController,
          IRecorderStatusSource
    {
        private readonly RecorderStatusHub _statuses = new(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        private long _revision;

        public RecorderState State => Current.State;

        public RecorderStatusSnapshot Current => _statuses.Current;

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            _statuses.Subscribe(subscriber);

        public Task<RecordingLifecycleStartResult> StartAsync(
            string? selectedServiceId,
            StartRecordingCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Publish(RecorderState.Arming);
            Publish(RecorderState.Recording);
            return Task.FromResult(new RecordingLifecycleStartResult(
                RecorderState.Recording,
                new VrChatCameraConnectionResolution.Connected(
                    new VrChatInstanceCandidate(
                        selectedServiceId ?? "status-service",
                        "VRChat status",
                        new Uri("http://127.0.0.1:9100/"),
                        "127.0.0.1",
                        9000),
                    new NoOpCameraGateway()),
                new StartRecordingResult.Started(
                    handle,
                    Task.CompletedTask,
                    Task.CompletedTask)));
        }

        public void Publish(RecorderState state) =>
            _statuses.TryPublish(RecorderStatusSnapshot.Create(
                ++_revision,
                state));

        public void Dispose() => _statuses.Dispose();
    }

    private sealed class PassiveStopRequestSink(
        StatusRecordingLifecycle lifecycle) : IStopRequestSink
    {
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requested.TrySetResult();
            return _completion.Task;
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;

        public void Complete()
        {
            lifecycle.Publish(RecorderState.Ready);
            _completion.TrySetResult();
        }
    }

    private sealed class NoOpCameraGateway : IVrChatCameraGateway
    {
        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)));

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ControllableRecordingLifecycle
        : IRecordingLifecycleController
    {
        private readonly Queue<
            TaskCompletionSource<RecordingLifecycleStartResult>> _starts = [];
        private readonly TaskCompletionSource _startRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecorderState State { get; private set; } = RecorderState.Ready;

        public RecorderState? NextStartStateOverride { get; init; }

        public List<(string? ServiceId, StartRecordingCommand Command)>
            StartRequests
        { get; } = [];

        public int DisposeCallCount { get; private set; }

        public TaskCompletionSource<RecordingLifecycleStartResult> EnqueuePending()
        {
            var completion = new TaskCompletionSource<RecordingLifecycleStartResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _starts.Enqueue(completion);
            return completion;
        }

        public void EnqueueCompleted(RecordingLifecycleStartResult result) =>
            EnqueuePending().SetResult(result);

        public async Task<RecordingLifecycleStartResult> StartAsync(
            string? selectedServiceId,
            StartRecordingCommand command,
            CancellationToken cancellationToken)
        {
            StartRequests.Add((selectedServiceId, command));
            State = RecorderState.Arming;
            _startRequested.TrySetResult();
            try
            {
                var result = await _starts
                    .Dequeue()
                    .Task
                    .WaitAsync(cancellationToken);
                State = NextStartStateOverride ?? result.State;
                return result;
            }
            catch (OperationCanceledException)
            {
                State = RecorderState.Ready;
                throw;
            }
        }

        public void SetState(RecorderState state) => State = state;

        public Task WaitUntilStartRequestedAsync() => _startRequested.Task;

        public void Dispose() => DisposeCallCount++;
    }

    private sealed class ControllableStopRequestSink(
        ControllableRecordingLifecycle lifecycle) : IStopRequestSink
    {
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CompleteImmediately { get; init; }

        public List<(RecordingStopRequest Request, CancellationToken Token)>
            Requests
        { get; } = [];

        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request, cancellationToken));
            lifecycle.SetState(RecorderState.Stopping);
            _requested.TrySetResult();
            if (CompleteImmediately)
            {
                lifecycle.SetState(RecorderState.Ready);
                return Task.CompletedTask;
            }

            return CompleteAsync(cancellationToken);
        }

        public void Complete() => _completed.TrySetResult();

        public void Fail(Exception failure) =>
            _completed.TrySetException(failure);

        public Task WaitUntilRequestedAsync() => _requested.Task;

        private async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await _completed.Task.WaitAsync(cancellationToken);
            lifecycle.SetState(RecorderState.Ready);
        }
    }
}
