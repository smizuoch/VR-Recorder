using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristOverlayInteractionHostTests
{
    [Fact]
    public async Task DispatchesPrimaryDownAgainstThePublishedLayout()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var snapshot = Snapshot(1);
        var target = Assert.Single(
            WristTextureLayoutEngine.Layout(
                snapshot,
                WristLayoutOptions.Default).HitTargets,
            item => item.Command == UiCommandId.ToggleRecording);
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            target.Bounds.Left + target.Bounds.Width / 2,
            target.Bounds.Top + target.Bounds.Height / 2,
            WristPointerButton.Primary,
            CursorIndex: 7));
        var host = Host(publisher, source, commands);

        var result = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.Texture.Published);
        Assert.True(result.Texture.BecameVisible);
        Assert.Equal(1, result.PointerEventsPolled);
        Assert.True(result.ActionDispatched);
        Assert.Single(commands.Commands);
        Assert.True(calls.IndexOf("publish") < calls.IndexOf("pointer"));
        Assert.True(calls.IndexOf("show") < calls.IndexOf("pointer"));
        Assert.True(calls.IndexOf("pointer") < calls.IndexOf("dispatch"));
    }

    [Fact]
    public async Task DoesNotPollWhenPublishingTheNewSnapshotFails()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls)
        {
            PublishFailuresRemaining = 1,
        };
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var host = Host(publisher, source, commands);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.TickAsync(
                Snapshot(2),
                TimeSpan.Zero,
                CancellationToken.None));

        Assert.Equal(0, source.PollCount);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task SuppressesDuplicateDownAndSameRevisionCommands()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var snapshot = Snapshot(3);
        var target = Assert.Single(
            WristTextureLayoutEngine.Layout(
                snapshot,
                WristLayoutOptions.Default).HitTargets,
            item => item.Command == UiCommandId.ToggleRecording);
        var down = new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            target.Bounds.Left + 1,
            target.Bounds.Top + 1,
            WristPointerButton.Primary,
            CursorIndex: 9);
        source.Events.Enqueue(down);
        source.Events.Enqueue(down);
        source.Events.Enqueue(down with { CursorIndex = 10 });
        var host = Host(publisher, source, commands);

        var first = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(3, first.PointerEventsPolled);
        Assert.True(first.ActionDispatched);
        Assert.Single(commands.Commands);

        source.Events.Enqueue(down with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        source.Events.Enqueue(down);
        var sameRevision = await host.TickAsync(
            snapshot,
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        Assert.False(sameRevision.ActionDispatched);
        Assert.Single(commands.Commands);

        source.Events.Enqueue(down with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        source.Events.Enqueue(down);
        var nextPresentation = await host.TickAsync(
            snapshot with { PresentationRevision = 1 },
            TimeSpan.FromMilliseconds(2),
            CancellationToken.None);

        Assert.True(nextPresentation.ActionDispatched);
        Assert.Equal(2, commands.Commands.Count);

        source.Events.Enqueue(down with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        source.Events.Enqueue(down);
        var nextRevision = await host.TickAsync(
            Snapshot(4),
            TimeSpan.FromMilliseconds(3),
            CancellationToken.None);

        Assert.True(nextRevision.ActionDispatched);
        Assert.Equal(3, commands.Commands.Count);
    }

    [Fact]
    public async Task BoundsPointerDrainPerTickAndIgnoresMoves()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        for (var index = 0;
             index < WristOverlayInteractionHost.MaxPointerEventsPerTick + 1;
             index++)
        {
            source.Events.Enqueue(new WristPointerEvent(
                WristPointerEventKind.Move,
                0,
                0,
                WristPointerButton.None,
                CursorIndex: 1));
        }
        var host = Host(publisher, source, commands);

        var first = await host.TickAsync(
            Snapshot(5),
            TimeSpan.Zero,
            CancellationToken.None);
        var second = await host.TickAsync(
            Snapshot(5),
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        Assert.Equal(
            WristOverlayInteractionHost.MaxPointerEventsPerTick,
            first.PointerEventsPolled);
        Assert.Equal(1, second.PointerEventsPolled);
        Assert.False(first.ActionDispatched);
        Assert.False(second.ActionDispatched);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task MoveTapOpensPositioningOnlyAfterPrimaryRelease()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var drags = new CapturingDragCommands();
        var snapshot = Snapshot(6);
        var target = Assert.Single(
            WristTextureLayoutEngine.Layout(
                snapshot,
                WristLayoutOptions.Default).HitTargets,
            item => item.Command == UiCommandId.OpenOverlayPositioning);
        var x = target.Bounds.Left + target.Bounds.Width / 2;
        var y = target.Bounds.Top + target.Bounds.Height / 2;
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            x,
            y,
            WristPointerButton.Primary,
            CursorIndex: 11));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonUp,
            x,
            y,
            WristPointerButton.Primary,
            CursorIndex: 11));
        var host = Host(publisher, source, commands, drags);

        var result = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.ActionDispatched);
        Assert.Equal(
            [(UiCommandId.OpenOverlayPositioning,
              UiActivationKind.WristRay)],
            commands.Commands);
        Assert.Empty(drags.Releases);
    }

    [Fact]
    public async Task MoveDragReleasesMetricDeltaWithoutOpeningPositioning()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var drags = new CapturingDragCommands();
        var snapshot = Snapshot(7);
        var target = Assert.Single(
            WristTextureLayoutEngine.Layout(
                snapshot,
                WristLayoutOptions.Default).HitTargets,
            item => item.Command == UiCommandId.OpenOverlayPositioning);
        var startX = target.Bounds.Left + 1;
        var startY = target.Bounds.Top + target.Bounds.Height / 2;
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            startX,
            startY,
            WristPointerButton.Primary,
            CursorIndex: 12));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.Move,
            startX + 560,
            startY + 32,
            WristPointerButton.None,
            CursorIndex: 12));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonUp,
            startX + 560,
            startY + 32,
            WristPointerButton.Primary,
            CursorIndex: 12));
        var host = Host(publisher, source, commands, drags);

        var result = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.ActionDispatched);
        var release = Assert.Single(drags.Releases);
        Assert.Equal(0.1203125, release.RightMeters, precision: 9);
        Assert.Equal(-0.006875, release.UpMeters, precision: 9);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task StopDownWinsOverDragReleaseInTheSameTick()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var drags = new CapturingDragCommands();
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(new RecorderStatusSnapshot(
                Revision: 8,
                RecorderState.Recording,
                RecorderAvailableActions.Stop));
        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);
        var move = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.OpenOverlayPositioning);
        var stop = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        var startX = move.Bounds.Left + 1;
        var startY = move.Bounds.Top + 1;
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            startX,
            startY,
            WristPointerButton.Primary,
            CursorIndex: 13));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.Move,
            startX + 560,
            startY,
            WristPointerButton.None,
            CursorIndex: 13));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonUp,
            startX + 560,
            startY,
            WristPointerButton.Primary,
            CursorIndex: 13));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            stop.Bounds.Left + 1,
            stop.Bounds.Top + 1,
            WristPointerButton.Primary,
            CursorIndex: 14));
        var host = Host(publisher, source, commands, drags);

        var result = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.ActionDispatched);
        Assert.Equal(
            [(UiCommandId.ToggleRecording, UiActivationKind.WristRay)],
            commands.Commands);
        Assert.Empty(drags.Releases);
    }

    [Fact]
    public async Task RejectsConcurrentTicksAndHonorsPreCanceledCalls()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new BlockingCommands();
        var snapshot = Snapshot(20);
        var target = Assert.Single(
            WristTextureLayoutEngine.Layout(
                snapshot,
                WristLayoutOptions.Default).HitTargets,
            item => item.Command == UiCommandId.ToggleRecording);
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            target.Bounds.Left + 1,
            target.Bounds.Top + 1,
            WristPointerButton.Primary,
            CursorIndex: 20));
        var host = Host(publisher, source, commands);

        var active = host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);
        await commands.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.TickAsync(
                snapshot,
                TimeSpan.FromMilliseconds(1),
                CancellationToken.None));
        commands.Release.TrySetResult();
        Assert.True((await active).ActionDispatched);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.TickAsync(
                Snapshot(21),
                TimeSpan.FromMilliseconds(2),
                canceled.Token));
    }

    [Fact]
    public async Task IgnoresSecondaryButtonsAndPrimaryHitsOutsideTheLayout()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            0,
            0,
            WristPointerButton.Secondary,
            CursorIndex: 22));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonDown,
            0,
            0,
            WristPointerButton.Primary,
            CursorIndex: 23));
        source.Events.Enqueue(new WristPointerEvent(
            WristPointerEventKind.ButtonUp,
            0,
            0,
            WristPointerButton.Primary,
            CursorIndex: 24));
        var host = Host(publisher, source, commands);

        var result = await host.TickAsync(
            Snapshot(22),
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(3, result.PointerEventsPolled);
        Assert.False(result.ActionDispatched);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task CommandDispatchSuppressesAnEarlierMoveGestureRelease()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var drags = new CapturingDragCommands();
        var snapshot = Snapshot(23);
        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);
        var move = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.OpenOverlayPositioning);
        var start = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        source.Events.Enqueue(Down(move, 25));
        source.Events.Enqueue(Down(start, 26));
        source.Events.Enqueue(Down(move, 25) with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        var host = Host(publisher, source, commands, drags);

        var result = await host.TickAsync(
            snapshot,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.ActionDispatched);
        Assert.Equal(
            [(UiCommandId.ToggleRecording, UiActivationKind.WristRay)],
            commands.Commands);
        Assert.Empty(drags.Releases);
    }

    [Fact]
    public async Task GestureReleaseDoesNotRepeatACommandedPresentationRevision()
    {
        var calls = new List<string>();
        var publisher = new FakePublisher(calls);
        var source = new FakePointerSource(calls);
        var commands = new CapturingCommands(calls);
        var drags = new CapturingDragCommands();
        var host = Host(publisher, source, commands, drags);
        var original = Snapshot(24);
        var originalLayout = WristTextureLayoutEngine.Layout(
            original,
            WristLayoutOptions.Default);
        var originalStart = Assert.Single(originalLayout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        source.Events.Enqueue(Down(originalStart, 27));
        Assert.True((await host.TickAsync(
            original,
            TimeSpan.Zero,
            CancellationToken.None)).ActionDispatched);

        var next = original with { PresentationRevision = 1 };
        var nextLayout = WristTextureLayoutEngine.Layout(
            next,
            WristLayoutOptions.Default);
        var move = Assert.Single(nextLayout.HitTargets, item =>
            item.Command == UiCommandId.OpenOverlayPositioning);
        var start = Assert.Single(nextLayout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        source.Events.Enqueue(Down(originalStart, 27) with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        source.Events.Enqueue(Down(move, 28));
        Assert.False((await host.TickAsync(
            next,
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None)).ActionDispatched);

        source.Events.Enqueue(Down(start, 29));
        Assert.True((await host.TickAsync(
            next,
            TimeSpan.FromMilliseconds(2),
            CancellationToken.None)).ActionDispatched);

        source.Events.Enqueue(Down(move, 28) with
        {
            Kind = WristPointerEventKind.ButtonUp,
        });
        var release = await host.TickAsync(
            next,
            TimeSpan.FromMilliseconds(3),
            CancellationToken.None);

        Assert.False(release.ActionDispatched);
        Assert.Equal(2, commands.Commands.Count);
        Assert.Empty(drags.Releases);
    }

    private static WristPointerEvent Down(
        WristHitTarget target,
        uint cursorIndex) =>
        new(
            WristPointerEventKind.ButtonDown,
            target.Bounds.Left + 1,
            target.Bounds.Top + 1,
            WristPointerButton.Primary,
            cursorIndex);

    private static WristOverlayInteractionHost Host(
        FakePublisher publisher,
        FakePointerSource source,
        IUiCommandDispatcher commands,
        CapturingDragCommands? drags = null) => new(
            new WristTextureUpdateHost(
                new WristTextureRenderer(
                    new OnePixelAssets(),
                    new WristTextureThemeSet(Theme(10), Theme(80))),
                WristLayoutOptions.Default,
                publisher),
            WristLayoutOptions.Default,
            new WristInputAdapter(commands),
            drags ?? new CapturingDragCommands(),
            source);

    private static WristUiSnapshot Snapshot(long revision) =>
        new WristUiProjector(EnglishUiLocalizer.Instance).Project(
            new RecorderStatusSnapshot(
                revision,
                RecorderState.Ready,
                RecorderAvailableActions.Start));

    private static WristTextureTheme Theme(byte seed) => new(
        new WristTexturePalette(
            Opaque(seed, 1),
            Opaque(seed, 2),
            Opaque(seed, 3),
            Opaque(seed, 4),
            Opaque(seed, 5),
            Opaque(seed, 6),
            Opaque(seed, 7),
            Opaque(seed, 8),
            Opaque(seed, 9),
            Opaque(seed, 10),
            Opaque(seed, 11)),
        new WristTextureMetrics(20, 28, 20, 12, 48, 72, 36));

    private static WristBgra32 Opaque(byte seed, byte offset) => new(
        (byte)(seed + offset),
        (byte)(seed + offset + 1),
        (byte)(seed + offset + 2),
        byte.MaxValue);

    private sealed class OnePixelAssets : IWristRasterAssetProvider
    {
        public bool TryRasterizeIcon(
            WristIconRasterRequest request,
            out WristAlphaMask? mask)
        {
            mask = new WristAlphaMask(1, 1, [byte.MaxValue]);
            return true;
        }

        public bool TryRasterizeText(
            WristTextRasterRequest request,
            out WristAlphaMask? mask)
        {
            mask = new WristAlphaMask(1, 1, [byte.MaxValue]);
            return true;
        }
    }

    private sealed class FakePublisher(List<string> calls)
        : IWristTexturePublisher
    {
        public int PublishFailuresRemaining { get; set; }

        public void Publish(WristTextureFrame frame)
        {
            calls.Add("publish");
            if (PublishFailuresRemaining > 0)
            {
                PublishFailuresRemaining--;
                throw new InvalidOperationException("publish failed");
            }
        }

        public void Show() => calls.Add("show");
    }

    private sealed class FakePointerSource(List<string> calls)
        : IWristPointerEventSource
    {
        public Queue<WristPointerEvent> Events { get; } = new();

        public int PollCount { get; private set; }

        public WristPointerEvent? PollPointerEvent()
        {
            PollCount++;
            if (Events.Count == 0)
            {
                return null;
            }
            calls.Add("pointer");
            return Events.Dequeue();
        }
    }

    private sealed class CapturingCommands(List<string> calls)
        : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind Activation)>
            Commands
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("dispatch");
            Commands.Add((command, activationKind));
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingCommands : IUiCommandDispatcher
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CapturingDragCommands
        : IWristOverlayDragDispatcher
    {
        public List<WristOverlayDragDelta> Releases { get; } = [];

        public Task ReleaseDragAsync(
            WristOverlayDragDelta delta,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Releases.Add(delta);
            return Task.CompletedTask;
        }
    }
}
