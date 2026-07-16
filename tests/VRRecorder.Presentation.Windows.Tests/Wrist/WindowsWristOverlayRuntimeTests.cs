using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Presentation.Wrist;
using VRRecorder.Presentation.Wrist.Windows;
using System.Threading.Channels;

namespace VRRecorder.Presentation.Windows.Tests.Wrist;

public sealed class WindowsWristOverlayRuntimeTests
{
    [Fact]
    public async Task PublishesAndShowsTheInitialProductionTexture()
    {
        var publisher = new CapturingPublisher();
        var runtime = new WindowsWristOverlayRuntime(
            new FixedStatusSource(new RecorderStatusSnapshot(
                Revision: 7,
                RecorderState.Ready,
                RecorderAvailableActions.Start)),
            new NoOpCommands(),
            new NoOpPlacementCommands(),
            publisher,
            new EmptyPointerSource(),
            EnglishUiLocalizer.Instance,
            WristLayoutOptions.Default,
            new BlockingClock());
        using var cancellation = new CancellationTokenSource();

        var run = runtime.RunAsync(cancellation.Token);
        var frame = await publisher.Published.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        await publisher.Visible.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(7, frame.Revision);
        Assert.True(frame.PixelWidth > 0);
        Assert.True(frame.PixelHeight > 0);
        Assert.Equal(frame.PixelWidth * 4, frame.StrideBytes);
        Assert.Contains(frame.BgraPixels.Span.ToArray(), value => value != 0);
        for (var index = 3;
             index < frame.BgraPixels.Length;
             index += 4)
        {
            Assert.Equal(byte.MaxValue, frame.BgraPixels.Span[index]);
        }
        Assert.Equal(1, publisher.ShowCount);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task MoveActivationPublishesPositioningWithoutRecorderChange()
    {
        var status = new RecorderStatusSnapshot(
            Revision: 8,
            RecorderState.Ready,
            RecorderAvailableActions.Start);
        var move = Assert.Single(
            WristTextureLayoutEngine.Layout(
                new WristUiProjector(EnglishUiLocalizer.Instance)
                    .Project(status),
                WristLayoutOptions.Default).HitTargets,
            target =>
                target.Command == UiCommandId.OpenOverlayPositioning);
        var pointer = new QueuedPointerSource();
        pointer.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            move.Bounds.Left + move.Bounds.Width / 2,
            move.Bounds.Top + move.Bounds.Height / 2,
            WristPointerButton.Primary,
            CursorIndex: 1));
        var publisher = new SequencedPublisher();
        var clock = new SteppingClock();
        var runtime = new WindowsWristOverlayRuntime(
            new FixedStatusSource(status),
            new NoOpCommands(),
            new NoOpPlacementCommands(),
            publisher,
            pointer,
            EnglishUiLocalizer.Instance,
            WristLayoutOptions.Default,
            clock);
        using var cancellation = new CancellationTokenSource();

        var run = runtime.RunAsync(cancellation.Token);
        var main = await publisher.NextAsync();
        var deadline = await clock.NextDeadlineAsync();
        clock.AdvanceTo(deadline);
        var positioning = await publisher.NextAsync();

        Assert.Equal(8, main.Revision);
        Assert.Equal(WristPage.Main, Page(main));
        Assert.Equal(8, positioning.Revision);
        Assert.Equal(WristPage.Positioning, Page(positioning));
        Assert.Equal(6, positioning.Layout.HitTargets.Count);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private static WristPage Page(WristTextureFrame frame) =>
        frame.Layout.HitTargets.Any(target =>
            target.Command == UiCommandId.CloseOverlayPositioning)
                ? WristPage.Positioning
                : WristPage.Main;

    private sealed class CapturingPublisher : IWristTexturePublisher
    {
        public TaskCompletionSource<WristTextureFrame> Published { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Visible { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ShowCount { get; private set; }

        public void Publish(WristTextureFrame frame) =>
            Published.TrySetResult(frame);

        public void Show()
        {
            ShowCount++;
            Visible.TrySetResult();
        }
    }

    private sealed class FixedStatusSource(RecorderStatusSnapshot current)
        : IRecorderStatusSource
    {
        public RecorderStatusSnapshot Current { get; } = current;

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            return NoOpDisposable.Instance;
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class NoOpCommands : IUiCommandDispatcher
    {
        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpPlacementCommands
        : IWristOverlayAdjustmentCommands
    {
        public Task<VrOverlayPlacement> NudgeAsync(
            WristOverlayNudgeDirection direction,
            WristOverlayNudgeSize size,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DefaultPlacement());
        }

        public Task<VrOverlayPlacement> RecenterAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DefaultPlacement());
        }

        public Task<VrOverlayPlacement> SetPlacementModeAsync(
            OverlayPlacementMode placementMode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DefaultPlacement() with
            {
                PlacementMode = placementMode,
            });
        }

        private static VrOverlayPlacement DefaultPlacement() => new(
            OverlayPlacementMode.WristDock,
            WristOverlayPoseContract.CreateDefaultWristDockTransform());
    }

    private sealed class EmptyPointerSource : IWristPointerEventSource
    {
        public WristPointerEvent? PollPointerEvent() => null;
    }

    private sealed class QueuedPointerSource : IWristPointerEventSource
    {
        public Queue<WristPointerEvent> Events { get; } = [];

        public WristPointerEvent? PollPointerEvent() =>
            Events.Count == 0 ? null : Events.Dequeue();
    }

    private sealed class BlockingClock : IMonotonicClock
    {
        public MonotonicTimestamp Now =>
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class SequencedPublisher : IWristTexturePublisher
    {
        private readonly Channel<WristTextureFrame> _frames =
            Channel.CreateUnbounded<WristTextureFrame>();

        public void Publish(WristTextureFrame frame) =>
            _frames.Writer.TryWrite(frame);

        public void Show()
        {
        }

        public async Task<WristTextureFrame> NextAsync() =>
            await _frames.Reader
                .ReadAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class SteppingClock : IMonotonicClock
    {
        private readonly object _gate = new();
        private readonly Channel<MonotonicTimestamp> _deadlines =
            Channel.CreateUnbounded<MonotonicTimestamp>();
        private MonotonicTimestamp _now =
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        private TaskCompletionSource? _delay;

        public MonotonicTimestamp Now
        {
            get
            {
                lock (_gate)
                {
                    return _now;
                }
            }
        }

        public async Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource delay;
            lock (_gate)
            {
                delay = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _delay = delay;
            }
            _deadlines.Writer.TryWrite(deadline);
            await delay.Task.WaitAsync(cancellationToken);
        }

        public async Task<MonotonicTimestamp> NextDeadlineAsync() =>
            await _deadlines.Reader
                .ReadAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));

        public void AdvanceTo(MonotonicTimestamp now)
        {
            TaskCompletionSource? delay;
            lock (_gate)
            {
                _now = now;
                delay = _delay;
                _delay = null;
            }
            delay?.TrySetResult();
        }
    }
}
