using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingRuntimeTests
{
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
        Assert.Equal(1, stops.Requests.Count);
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
    public async Task ConcurrentTogglesJoinOneStartOperation()
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

        Assert.Equal(1, lifecycle.StartRequests.Count);
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

    private sealed class ControllableRecordingLifecycle
        : IRecordingLifecycleController
    {
        private readonly Queue<
            TaskCompletionSource<RecordingLifecycleStartResult>> _starts = [];
        private readonly TaskCompletionSource _startRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecorderState State { get; private set; } = RecorderState.Ready;

        public List<(string? ServiceId, StartRecordingCommand Command)>
            StartRequests { get; } = [];

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
                State = result.State;
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
            Requests { get; } = [];

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

        public Task WaitUntilRequestedAsync() => _requested.Task;

        private async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await _completed.Task.WaitAsync(cancellationToken);
            lifecycle.SetState(RecorderState.Ready);
        }
    }
}
