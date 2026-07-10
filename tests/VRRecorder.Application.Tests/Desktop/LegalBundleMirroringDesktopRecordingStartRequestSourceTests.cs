using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class LegalBundleMirroringDesktopRecordingStartRequestSourceTests
{
    [Fact]
    public async Task MirrorsResolvedOutputBeforeReturningSameRequest()
    {
        var events = new List<string>();
        var request = Request();
        var inner = new StubRequestSource(request, events);
        var mirror = new ControllableLegalBundleOutputMirror(events);
        var source = new LegalBundleMirroringDesktopRecordingStartRequestSource(
            inner,
            mirror);
        using var cancellation = new CancellationTokenSource();

        var result = await source.GetAsync(cancellation.Token);

        Assert.Same(request, result);
        Assert.Equal(["request", "mirror"], events);
        var invocation = Assert.Single(mirror.Invocations);
        Assert.Same(request.Command.OutputPath, invocation.OutputPath);
        Assert.Equal(cancellation.Token, invocation.CancellationToken);
    }

    [Fact]
    public async Task RequestRemainsIncompleteUntilMirrorCompletes()
    {
        var request = Request();
        var mirror = new ControllableLegalBundleOutputMirror
        {
            HoldUntilReleased = true,
        };
        var source = new LegalBundleMirroringDesktopRecordingStartRequestSource(
            new StubRequestSource(request),
            mirror);

        var loading = source.GetAsync(CancellationToken.None);
        await mirror.WaitUntilInvokedAsync();

        Assert.False(loading.IsCompleted);
        mirror.Release();
        Assert.Same(request, await loading);
    }

    [Fact]
    public async Task MirrorFailurePreventsLifecycleOscAndCameraPipelineStart()
    {
        var failure = new IOException("output Legal Bundle mirror failed");
        var inner = new StubRequestSource(Request());
        var mirror = new ControllableLegalBundleOutputMirror
        {
            Failure = failure,
        };
        var lifecycle = new NeverStartedRecordingLifecycle();
        await using var runtime = new DesktopRecordingRuntime(
            new LegalBundleMirroringDesktopRecordingStartRequestSource(
                inner,
                mirror),
            lifecycle,
            new RejectingStopRequestSink());

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            runtime.ToggleAsync(CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Single(mirror.Invocations);
        Assert.Equal(0, lifecycle.StartCallCount);
    }

    [Fact]
    public async Task CancellationDuringMirrorPreventsPipelineStart()
    {
        var inner = new StubRequestSource(Request());
        var mirror = new ControllableLegalBundleOutputMirror
        {
            HoldUntilCancellation = true,
        };
        var lifecycle = new NeverStartedRecordingLifecycle();
        await using var runtime = new DesktopRecordingRuntime(
            new LegalBundleMirroringDesktopRecordingStartRequestSource(
                inner,
                mirror),
            lifecycle,
            new RejectingStopRequestSink());
        using var cancellation = new CancellationTokenSource();

        var starting = runtime.ToggleAsync(cancellation.Token);
        await mirror.WaitUntilInvokedAsync();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Single(mirror.Invocations);
        Assert.Equal(0, lifecycle.StartCallCount);
    }

    private static DesktopRecordingStartRequest Request() =>
        new(
            "vrc-selected",
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.Combine(
                    Path.GetTempPath(),
                    "vr-recorder-legal-mirror-output")),
                new FrameRate(30)));

    private sealed class StubRequestSource(
        DesktopRecordingStartRequest request,
        List<string>? events = null)
        : IDesktopRecordingStartRequestSource
    {
        public Task<DesktopRecordingStartRequest> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events?.Add("request");
            return Task.FromResult(request);
        }
    }

    private sealed class ControllableLegalBundleOutputMirror(
        List<string>? events = null) : ILegalBundleOutputMirror
    {
        private readonly TaskCompletionSource _invoked = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(OutputPath OutputPath, CancellationToken CancellationToken)>
            Invocations
        { get; } = [];

        public Exception? Failure { get; init; }

        public bool HoldUntilCancellation { get; init; }

        public bool HoldUntilReleased { get; init; }

        public async Task MirrorAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            events?.Add("mirror");
            Invocations.Add((outputPath, cancellationToken));
            _invoked.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }

            if (HoldUntilCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (HoldUntilReleased)
            {
                await _released.Task.WaitAsync(cancellationToken);
            }
        }

        public Task WaitUntilInvokedAsync() => _invoked.Task;

        public void Release() => _released.TrySetResult();
    }

    private sealed class NeverStartedRecordingLifecycle
        : IRecordingLifecycleController
    {
        private readonly RecorderStatusHub _statuses = new(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));

        public RecorderState State => RecorderState.Ready;

        public RecorderStatusSnapshot Current => _statuses.Current;

        public int StartCallCount { get; private set; }

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            _statuses.Subscribe(subscriber);

        public Task<RecordingLifecycleStartResult> StartAsync(
            string? selectedServiceId,
            StartRecordingCommand command,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            throw new InvalidOperationException(
                "The lifecycle/OSC/camera pipeline must not start.");
        }

        public void Dispose()
        {
            _statuses.Dispose();
        }
    }

    private sealed class RejectingStopRequestSink : IStopRequestSink
    {
        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "No stop request exists before recording starts.");
    }
}
