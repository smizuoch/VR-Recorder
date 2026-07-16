using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristTextureUpdateHostTests
{
    [Fact]
    public void PublishesBeforeShowingAndCommitsOnlyNewRevisions()
    {
        var publisher = new FakePublisher();
        var host = Host(publisher);

        var first = host.Tick(Snapshot(1, RecorderState.Ready), TimeSpan.Zero);
        var unchanged = host.Tick(
            Snapshot(1, RecorderState.Ready),
            TimeSpan.FromSeconds(1));
        var changed = host.Tick(
            Snapshot(2, RecorderState.Ready),
            TimeSpan.FromSeconds(1));

        Assert.True(first.Published);
        Assert.True(first.BecameVisible);
        Assert.False(unchanged.Published);
        Assert.False(unchanged.BecameVisible);
        Assert.True(changed.Published);
        Assert.False(changed.BecameVisible);
        Assert.Equal(
            ["publish:1", "show", "publish:2"],
            publisher.Calls);
    }

    [Fact]
    public void RetriesTheSameRevisionWhenPublishFails()
    {
        var publisher = new FakePublisher { PublishFailuresRemaining = 1 };
        var host = Host(publisher);
        var snapshot = Snapshot(3, RecorderState.Ready);

        Assert.Throws<InvalidOperationException>(() =>
            host.Tick(snapshot, TimeSpan.Zero));
        var retry = host.Tick(snapshot, TimeSpan.FromMilliseconds(1));

        Assert.True(retry.Published);
        Assert.True(retry.BecameVisible);
        Assert.Equal(
            ["publish:3", "publish:3", "show"],
            publisher.Calls);
    }

    [Fact]
    public void RepublishesTheFirstFrameWhenShowFails()
    {
        var publisher = new FakePublisher { ShowFailuresRemaining = 1 };
        var host = Host(publisher);
        var snapshot = Snapshot(4, RecorderState.Ready);

        Assert.Throws<InvalidOperationException>(() =>
            host.Tick(snapshot, TimeSpan.Zero));
        var retry = host.Tick(snapshot, TimeSpan.FromMilliseconds(1));

        Assert.True(retry.Published);
        Assert.True(retry.BecameVisible);
        Assert.Equal(
            ["publish:4", "show", "publish:4", "show"],
            publisher.Calls);
    }

    [Fact]
    public void PublishesRecordingTelemetryAtMostEveryHundredMilliseconds()
    {
        var publisher = new FakePublisher();
        var host = Host(publisher);
        var snapshot = Snapshot(5, RecorderState.Recording);

        host.Tick(snapshot, TimeSpan.Zero);
        var early = host.Tick(snapshot, TimeSpan.FromMilliseconds(99));
        var due = host.Tick(snapshot, TimeSpan.FromMilliseconds(100));

        Assert.False(early.Published);
        Assert.True(due.Published);
        Assert.Equal(WristTextureUpdateReason.RecordingHeartbeat, due.Reason);
        Assert.Equal(["publish:5", "show", "publish:5"], publisher.Calls);
    }

    private static WristTextureUpdateHost Host(FakePublisher publisher) =>
        new(
            new WristTextureRenderer(
                new OnePixelRasterAssets(),
                new WristTextureThemeSet(Theme(10), Theme(80))),
            WristLayoutOptions.Default,
            publisher);

    private static WristUiSnapshot Snapshot(
        long revision,
        RecorderState state) =>
        new WristUiProjector(EnglishUiLocalizer.Instance).Project(
            new RecorderStatusSnapshot(
                revision,
                state,
                RecorderAvailableActions.None));

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

    private static WristBgra32 Opaque(byte seed, byte offset) =>
        new(
            (byte)(seed + offset),
            (byte)(seed + offset + 1),
            (byte)(seed + offset + 2),
            byte.MaxValue);

    private sealed class OnePixelRasterAssets : IWristRasterAssetProvider
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

    private sealed class FakePublisher : IWristTexturePublisher
    {
        public List<string> Calls { get; } = [];

        public int PublishFailuresRemaining { get; set; }

        public int ShowFailuresRemaining { get; set; }

        public void Publish(WristTextureFrame frame)
        {
            Calls.Add($"publish:{frame.Revision}");
            if (PublishFailuresRemaining > 0)
            {
                PublishFailuresRemaining--;
                throw new InvalidOperationException("publish failed");
            }
        }

        public void Show()
        {
            Calls.Add("show");
            if (ShowFailuresRemaining > 0)
            {
                ShowFailuresRemaining--;
                throw new InvalidOperationException("show failed");
            }
        }
    }
}
