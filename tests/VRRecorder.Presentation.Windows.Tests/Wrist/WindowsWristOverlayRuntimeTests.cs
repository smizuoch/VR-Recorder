using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Presentation.Wrist;
using VRRecorder.Presentation.Wrist.Windows;

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

    private sealed class EmptyPointerSource : IWristPointerEventSource
    {
        public WristPointerEvent? PollPointerEvent() => null;
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
}
