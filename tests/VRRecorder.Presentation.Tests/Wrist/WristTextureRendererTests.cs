using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristTextureRendererTests
{
    [Fact]
    public void RendersDeterministicOpaqueBgraFromResolvedAssets()
    {
        var assets = new SyntheticRasterAssets();
        var renderer = new WristTextureRenderer(
            assets,
            new WristTextureThemeSet(CreateTheme(10), CreateTheme(80)));
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(RecorderStatusSnapshot.Create(
                7,
                RecorderState.Ready));

        var first = renderer.Render(snapshot, WristLayoutOptions.Default);
        var second = renderer.Render(snapshot, WristLayoutOptions.Default);

        Assert.Equal(1024, first.PixelWidth);
        Assert.Equal(512, first.PixelHeight);
        Assert.Equal(4096, first.StrideBytes);
        Assert.Equal(1024 * 512 * 4, first.BgraPixels.Length);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
        Assert.True(first.BgraPixels.Span.SequenceEqual(
            second.BgraPixels.Span));
        for (var index = 3;
             index < first.BgraPixels.Length;
             index += 4)
        {
            Assert.Equal(byte.MaxValue, first.BgraPixels.Span[index]);
        }

        Assert.Contains(assets.IconRequests, request =>
            request.SemanticId == "recording.ready");
        Assert.Contains(assets.IconRequests, request =>
            request.SemanticId == "recording.start");
        Assert.Contains(assets.TextRequests, request =>
            request.Text == "Ready");
        Assert.Contains(assets.TextRequests, request =>
            request.Text == "REC");
        Assert.Equal(
            "10EB32FA1B49CF1982729B3F1416ED1C8E410870FEDC1D9875DEE5655BEA5875",
            first.Sha256Hex);
    }

    [Fact]
    public void UsesHighContrastRtlAndTwoHundredPercentTextRequests()
    {
        var normal = CreateTheme(10);
        var highContrast = CreateTheme(80);
        var assets = new SyntheticRasterAssets();
        var renderer = new WristTextureRenderer(
            assets,
            new WristTextureThemeSet(normal, highContrast));
        var snapshot = new WristUiProjector(JapaneseUiLocalizer.Instance)
            .Project(RecorderStatusSnapshot.Create(
                8,
                RecorderState.Ready));
        var options = WristLayoutOptions.Default with
        {
            FlowDirection = WristFlowDirection.RightToLeft,
            TextScale = 2.0,
            HighContrast = true,
        };

        var frame = renderer.Render(snapshot, options);

        Assert.Equal(highContrast.Palette.Surface.B, frame.BgraPixels.Span[0]);
        Assert.Equal(highContrast.Palette.Surface.G, frame.BgraPixels.Span[1]);
        Assert.Equal(highContrast.Palette.Surface.R, frame.BgraPixels.Span[2]);
        Assert.Contains(assets.TextRequests, request =>
            request.Text == "準備完了" &&
            request.TextScale == 2.0 &&
            request.FlowDirection == WristFlowDirection.RightToLeft);
    }

    [Fact]
    public void MissingRasterAssetFailsClosedBeforeReturningPixels()
    {
        var assets = new SyntheticRasterAssets
        {
            MissingSemanticId = "recording.start",
        };
        var renderer = new WristTextureRenderer(
            assets,
            new WristTextureThemeSet(CreateTheme(10), CreateTheme(80)));
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(RecorderStatusSnapshot.Create(
                9,
                RecorderState.Ready));

        var exception = Assert.Throws<WristRasterAssetMissingException>(() =>
            renderer.Render(snapshot, WristLayoutOptions.Default));

        Assert.Equal("recording.start", exception.AssetId);
    }

    [Fact]
    public void RendersEveryPositioningControl()
    {
        var assets = new SyntheticRasterAssets();
        var renderer = new WristTextureRenderer(
            assets,
            new WristTextureThemeSet(CreateTheme(10), CreateTheme(80)));
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                new RecorderStatusSnapshot(
                    Revision: 10,
                    RecorderState.Ready,
                    RecorderAvailableActions.Start),
                WristPage.Positioning);

        var frame = renderer.Render(snapshot, WristLayoutOptions.Default);

        Assert.Equal(8, frame.Layout.HitTargets.Count);
        Assert.Equal(
            snapshot.Actions.Select(action => action.IconSemanticId),
            assets.IconRequests
                .Where(request => request.SemanticId.StartsWith(
                    "overlay.",
                    StringComparison.Ordinal) ||
                    request.SemanticId == "common.back")
                .Select(request => request.SemanticId));
    }

    private static WristTextureTheme CreateTheme(byte seed) => new(
        new WristTexturePalette(
            Surface: Opaque(seed, 1),
            OnSurface: Opaque(seed, 2),
            SurfaceContainer: Opaque(seed, 3),
            Recording: Opaque(seed, 4),
            OnRecording: Opaque(seed, 5),
            Error: Opaque(seed, 6),
            OnError: Opaque(seed, 7),
            Selected: Opaque(seed, 8),
            OnSelected: Opaque(seed, 9),
            Disabled: Opaque(seed, 10),
            OnDisabled: Opaque(seed, 11)),
        new WristTextureMetrics(
            StateCornerRadiusPixels: 20,
            ActionCornerRadiusPixels: 28,
            ContentPaddingPixels: 20,
            ContentGapPixels: 12,
            StateIconPixels: 40,
            PrimaryIconPixels: 72,
            SecondaryIconPixels: 40));

    private static WristBgra32 Opaque(byte seed, byte offset) => new(
        B: checked((byte)(seed + offset)),
        G: checked((byte)(seed + offset + 1)),
        R: checked((byte)(seed + offset + 2)),
        A: byte.MaxValue);

    private sealed class SyntheticRasterAssets : IWristRasterAssetProvider
    {
        public List<WristIconRasterRequest> IconRequests { get; } = [];

        public List<WristTextRasterRequest> TextRequests { get; } = [];

        public string? MissingSemanticId { get; init; }

        public bool TryRasterizeIcon(
            WristIconRasterRequest request,
            out WristAlphaMask? mask)
        {
            IconRequests.Add(request);
            if (request.SemanticId == MissingSemanticId)
            {
                mask = null;
                return false;
            }

            mask = SolidMask(request.PixelSize, request.PixelSize);
            return true;
        }

        public bool TryRasterizeText(
            WristTextRasterRequest request,
            out WristAlphaMask? mask)
        {
            TextRequests.Add(request);
            var width = Math.Min(
                request.MaximumWidthPixels,
                Math.Max(1, checked(request.Text.Length * 8)));
            var height = Math.Max(1, checked((int)(12 * request.TextScale)));
            mask = SolidMask(width, height);
            return true;
        }

        private static WristAlphaMask SolidMask(int width, int height) =>
            new(width, height, Enumerable.Repeat(byte.MaxValue, width * height));
    }
}
