using VRRecorder.Application.Presentation;
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
        var target = Assert.Single(WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default).HitTargets);
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
        var target = Assert.Single(WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default).HitTargets);
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
        var nextRevision = await host.TickAsync(
            Snapshot(4),
            TimeSpan.FromMilliseconds(2),
            CancellationToken.None);

        Assert.True(nextRevision.ActionDispatched);
        Assert.Equal(2, commands.Commands.Count);
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

    private static WristOverlayInteractionHost Host(
        FakePublisher publisher,
        FakePointerSource source,
        CapturingCommands commands) => new(
            new WristTextureUpdateHost(
                new WristTextureRenderer(
                    new OnePixelAssets(),
                    new WristTextureThemeSet(Theme(10), Theme(80))),
                WristLayoutOptions.Default,
                publisher),
            WristLayoutOptions.Default,
            new WristInputAdapter(commands),
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
            Commands { get; } = [];

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
}
