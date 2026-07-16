using System.IO;
using VRRecorder.Presentation.Wrist;
using VRRecorder.Presentation.Wrist.Windows;

namespace VRRecorder.Presentation.Windows.Tests.Wrist;

public sealed class WindowsWristRasterAssetProviderTests
{
    [Fact]
    public void ResolvesAssetsStagedBesideTheApplication()
    {
        var provider = new WindowsWristRasterAssetProvider();

        var found = provider.TryRasterizeIcon(
            new WristIconRasterRequest(
                "recording.ready",
                PixelSize: 48,
                IsSelected: false,
                WristFlowDirection.LeftToRight),
            out var mask);

        Assert.True(found);
        Assert.NotNull(mask);
        Assert.Contains(mask.Alpha.Span.ToArray(), alpha => alpha != 0);
    }

    [Fact]
    public void RasterizesEveryProductionIconSemanticId()
    {
        var provider = new WindowsWristRasterAssetProvider(AssetRoot());

        foreach (var semanticId in
                 WindowsWristRasterAssetProvider.ProductionSemanticIds)
        {
            var found = provider.TryRasterizeIcon(
                new WristIconRasterRequest(
                    semanticId,
                    PixelSize: 72,
                    IsSelected: false,
                    WristFlowDirection.LeftToRight),
                out var mask);

            Assert.True(found, semanticId);
            Assert.NotNull(mask);
            Assert.InRange(mask.Width, 1, 72);
            Assert.InRange(mask.Height, 1, 72);
            Assert.Contains(mask.Alpha.Span.ToArray(), alpha => alpha != 0);
        }
    }

    [Theory]
    [InlineData("recording.start.short", "REC")]
    [InlineData("recording.start.short", "録画")]
    public void RasterizesLocalizedTextWithTheWindowsSystemFont(
        string assetId,
        string text)
    {
        var provider = new WindowsWristRasterAssetProvider(AssetRoot());

        var found = provider.TryRasterizeText(
            new WristTextRasterRequest(
                assetId,
                text,
                WristTextRole.PrimaryAction,
                TextScale: 1.0,
                MaximumWidthPixels: 200,
                WristFlowDirection.LeftToRight),
            out var mask);

        Assert.True(found);
        Assert.NotNull(mask);
        Assert.InRange(mask.Width, 1, 200);
        Assert.True(mask.Height > 0);
        Assert.Contains(mask.Alpha.Span.ToArray(), alpha => alpha != 0);
    }

    [Fact]
    public void RejectsUnknownIconsWithoutFallingBack()
    {
        var provider = new WindowsWristRasterAssetProvider(AssetRoot());

        var found = provider.TryRasterizeIcon(
            new WristIconRasterRequest(
                "unknown.icon",
                PixelSize: 48,
                IsSelected: false,
                WristFlowDirection.LeftToRight),
            out var mask);

        Assert.False(found);
        Assert.Null(mask);
    }

    [Theory]
    [InlineData("overlay.move")]
    [InlineData("overlay.nudge.up")]
    [InlineData("overlay.nudge.down")]
    [InlineData("overlay.nudge.left")]
    [InlineData("overlay.nudge.right")]
    [InlineData("overlay.recenter")]
    [InlineData("common.back")]
    public void RasterizesPositioningIcons(string semanticId)
    {
        var provider = new WindowsWristRasterAssetProvider(AssetRoot());

        var found = provider.TryRasterizeIcon(
            new WristIconRasterRequest(
                semanticId,
                PixelSize: 48,
                IsSelected: false,
                WristFlowDirection.LeftToRight),
            out var mask);

        Assert.True(found);
        Assert.NotNull(mask);
        Assert.Contains(mask.Alpha.Span.ToArray(), alpha => alpha != 0);
    }

    [Fact]
    public void RejectsTamperedProductionIcon()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-wrist-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(
                Path.Combine(AssetRoot(), "recording-start.svg"),
                Path.Combine(root, "recording-start.svg"));
            File.AppendAllText(
                Path.Combine(root, "recording-start.svg"),
                "tampered");
            var provider = new WindowsWristRasterAssetProvider(root);

            Assert.Throws<InvalidDataException>(() =>
                provider.TryRasterizeIcon(
                    new WristIconRasterRequest(
                        "recording.start",
                        PixelSize: 48,
                        IsSelected: false,
                        WristFlowDirection.LeftToRight),
                    out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string AssetRoot() => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "VRRecorder.Presentation.Wrist.Windows",
        "Assets",
        "MaterialSymbols");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "The VR-Recorder repository root was not found.");
    }
}
