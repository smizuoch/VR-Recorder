using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Wrist.Windows;

public sealed class WindowsWristRasterAssetProvider
    : IWristRasterAssetProvider
{
    private static readonly IReadOnlyDictionary<string, IconAsset> Icons =
        new Dictionary<string, IconAsset>(StringComparer.Ordinal)
        {
            ["recording.start"] = new(
                "recording-start.svg",
                "6728a2994c9b2af376f7ed26e2d687fcd60ade3a5b6d6e5ecb7ed68cbc32f507"),
            ["recording.stop"] = new(
                "recording-stop.svg",
                "36824abeb5075a3a52b3bead3a7bf2635bb2460f8ed0743581c623953ef98219"),
            ["audio.microphone.on"] = new(
                "microphone-on.svg",
                "ec58d37bbdc6bb21f5f2bc30e54243d72cabd37214babea03550db8d6a668ba1"),
            ["audio.microphone.off"] = new(
                "microphone-off.svg",
                "4039a8b32516dcd5d16e3d2c75d7e143dc5ccbdf74b2d4e9332525a2cb67f6b8"),
            ["audio.microphone.unavailable"] =
                new(
                    "microphone-off.svg",
                    "4039a8b32516dcd5d16e3d2c75d7e143dc5ccbdf74b2d4e9332525a2cb67f6b8"),
            ["audio.muteAll"] = new(
                "mute-all.svg",
                "68011667853aec9a87f016bb2e0498f6969c4221291f1eb0a80bc0c47154cb7f"),
            ["camera.retry"] = new(
                "retry.svg",
                "346e99f9447c9ce850adc4b892df665fd7064dc9b8ca718c41d15b1904c76db6"),
            ["overlay.move"] = new(
                "drag-indicator.svg",
                "064a5e0aab0df33a1f4f11c4c7d409410868c4e130c34cf432658f1a9f2699d5"),
            ["overlay.nudge.up"] = new(
                "arrow-up.svg",
                "b13097ba84a1951f65afafcc3d67386f56642abfc2d9812769614a37abfbf8ec"),
            ["overlay.nudge.down"] = new(
                "arrow-down.svg",
                "06225b24a2e9365d1cd88f5e472e733ec8918505f89e174976ea259c22769726"),
            ["overlay.nudge.left"] = new(
                "arrow-back.svg",
                "bc1b08f9d7572132065ae688832368bdc17b609395bdf169c9558fe54d01c0a9"),
            ["overlay.nudge.right"] = new(
                "arrow-forward.svg",
                "55ca246a23f5072843e8e2b3062c30fd03e8c1cbc90c95905c13b5a8cdb2b428"),
            ["overlay.recenter"] = new(
                "center-focus-strong.svg",
                "600bae74e809ff62808d421106c9b65a8a081b226616352e40b159745df7bf85"),
            ["common.back"] = new(
                "arrow-back.svg",
                "bc1b08f9d7572132065ae688832368bdc17b609395bdf169c9558fe54d01c0a9",
                MirrorInRtl: true),
            ["system.booting"] = new(
                "progress.svg",
                "207d2e37e41feacc1baf2a31a4f6d459bfdeef22a267bbf0ad13d2086c422824"),
            ["legal.error"] = new(
                "warning.svg",
                "e2e4140e51d76591022d99a8548871258093cb7c7a5a20222303af750cdfd56c"),
            ["recording.ready"] = new(
                "ready.svg",
                "13beafe0fecb396083f396bf286b2a7d1f9510680dc6834b760d7fedc2b0bb88"),
            ["camera.arming"] = new(
                "camera.svg",
                "651b10af3e2488db7df5026cb049b901550b230832224cd7cb784534c78e6231"),
            ["recording.countdown"] = new(
                "timer.svg",
                "5effee21e31c4df283cc65b8d3cb57867cbebb76d6c319b5e453522b79f800e4"),
            ["recording.starting"] = new(
                "progress.svg",
                "207d2e37e41feacc1baf2a31a4f6d459bfdeef22a267bbf0ad13d2086c422824"),
            ["recording.active"] = new(
                "recording-start.svg",
                "6728a2994c9b2af376f7ed26e2d687fcd60ade3a5b6d6e5ecb7ed68cbc32f507"),
            ["camera.signal-lost"] = new(
                "signal-disconnected.svg",
                "fb3a4fa0954fba80cb330c37572d4c0ab6b3b2c7dd013c1d20f48df0b687986d"),
            ["recording.stopping"] = new(
                "stopping.svg",
                "336c9c86e5064275c586be8b085bebcc1c516502e8a7f7518928e2abfbb73852"),
            ["camera.no-signal"] = new(
                "signal-disconnected.svg",
                "fb3a4fa0954fba80cb330c37572d4c0ab6b3b2c7dd013c1d20f48df0b687986d"),
            ["system.error"] = new(
                "warning.svg",
                "e2e4140e51d76591022d99a8548871258093cb7c7a5a20222303af750cdfd56c"),
        };

    private readonly string _assetRoot;
    private readonly ConcurrentDictionary<IconCacheKey, WristAlphaMask>
        _iconCache = new();
    private readonly ConcurrentDictionary<TextCacheKey, WristAlphaMask>
        _textCache = new();

    public WindowsWristRasterAssetProvider()
        : this(Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "MaterialSymbols"))
    {
    }

    public WindowsWristRasterAssetProvider(string assetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        _assetRoot = Path.GetFullPath(assetRoot);
    }

    public static IReadOnlyList<string> ProductionSemanticIds { get; } =
        Icons.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool TryRasterizeIcon(
        WristIconRasterRequest request,
        out WristAlphaMask? mask)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SemanticId);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.PixelSize, 1);
        if (!Icons.TryGetValue(request.SemanticId, out var asset))
        {
            mask = null;
            return false;
        }

        var path = Path.Combine(_assetRoot, asset.FileName);
        if (!File.Exists(path))
        {
            mask = null;
            return false;
        }

        mask = _iconCache.GetOrAdd(
            new IconCacheKey(
                request.SemanticId,
                request.PixelSize,
                request.IsSelected,
                request.FlowDirection),
            _ =>
            {
                VerifySha256(path, asset.Sha256);
                return RasterizeSvg(
                    path,
                    request.PixelSize,
                    asset.MirrorInRtl &&
                    request.FlowDirection ==
                    WristFlowDirection.RightToLeft);
            });
        return true;
    }

    public bool TryRasterizeText(
        WristTextRasterRequest request,
        out WristAlphaMask? mask)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        if (!double.IsFinite(request.TextScale) || request.TextScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(
            request.MaximumWidthPixels,
            1);

        mask = _textCache.GetOrAdd(
            new TextCacheKey(
                request.AssetId,
                request.Text,
                request.Role,
                request.TextScale,
                request.MaximumWidthPixels,
                request.FlowDirection),
            static key => RasterizeText(key));
        return true;
    }

    private static WristAlphaMask RasterizeSvg(
        string path,
        int pixelSize,
        bool mirror)
    {
        var document = ReadSvg(path);
        var geometry = Geometry.Parse(document.PathData);
        geometry.Freeze();
        var scale = Math.Min(
            pixelSize / document.ViewBox.Width,
            pixelSize / document.ViewBox.Height);
        var contentWidth = document.ViewBox.Width * scale;
        var contentHeight = document.ViewBox.Height * scale;
        var offsetX = (pixelSize - contentWidth) / 2;
        var offsetY = (pixelSize - contentHeight) / 2;
        var matrix = new Matrix(
            mirror ? -scale : scale,
            0,
            0,
            scale,
            mirror
                ? offsetX + (document.ViewBox.X +
                             document.ViewBox.Width) * scale
                : offsetX - document.ViewBox.X * scale,
            offsetY - document.ViewBox.Y * scale);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(matrix));
            context.DrawGeometry(Brushes.White, null, geometry);
            context.Pop();
        }

        return RenderMask(drawing, pixelSize, pixelSize);
    }

    private static WristAlphaMask RasterizeText(TextCacheKey key)
    {
        var fontSize = key.Role switch
        {
            WristTextRole.State => 28.0,
            WristTextRole.PrimaryAction => 24.0,
            WristTextRole.SecondaryAction => 18.0,
            _ => throw new ArgumentOutOfRangeException(
                nameof(key),
                key.Role,
                null),
        } * key.TextScale;
        var flowDirection =
            key.FlowDirection == WristFlowDirection.RightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        var text = new FormattedText(
            key.Text,
            CultureInfo.CurrentUICulture,
            flowDirection,
            new Typeface(
                SystemFonts.MessageFontFamily,
                FontStyles.Normal,
                key.Role == WristTextRole.PrimaryAction
                    ? FontWeights.SemiBold
                    : FontWeights.Medium,
                FontStretches.Normal),
            fontSize,
            Brushes.White,
            pixelsPerDip: 1.0)
        {
            MaxLineCount = 1,
            MaxTextWidth = key.MaximumWidthPixels,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var width = Math.Max(
            1,
            Math.Min(
                key.MaximumWidthPixels,
                checked((int)Math.Ceiling(text.WidthIncludingTrailingWhitespace))));
        var height = Math.Max(1, checked((int)Math.Ceiling(text.Height)));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawText(text, new Point(0, 0));
        }

        return RenderMask(drawing, width, height);
    }

    private static WristAlphaMask RenderMask(
        DrawingVisual drawing,
        int width,
        int height)
    {
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var stride = checked(width * 4);
        var bgra = new byte[checked(stride * height)];
        bitmap.CopyPixels(bgra, stride, 0);
        var alpha = new byte[checked(width * height)];
        for (var index = 0; index < alpha.Length; index++)
        {
            alpha[index] = bgra[index * 4 + 3];
        }

        return new WristAlphaMask(width, height, alpha);
    }

    private static void VerifySha256(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert
            .ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The wrist icon SHA-256 does not match its allowlist: {path}");
        }
    }

    private static SvgDocument ReadSvg(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = 1_000_000,
            XmlResolver = null,
        });
        var root = XDocument.Load(reader, LoadOptions.None).Root;
        if (root is null ||
            !string.Equals(root.Name.LocalName, "svg", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The wrist icon is not an SVG document: {path}");
        }

        var viewBox = ParseViewBox(
            root.Attribute("viewBox")?.Value,
            path);
        var paths = root
            .Descendants()
            .Where(element =>
                string.Equals(
                    element.Name.LocalName,
                    "path",
                    StringComparison.Ordinal))
            .ToArray();
        if (paths.Length != 1 ||
            string.IsNullOrWhiteSpace(paths[0].Attribute("d")?.Value))
        {
            throw new InvalidDataException(
                $"The wrist icon must contain exactly one SVG path: {path}");
        }

        return new SvgDocument(
            viewBox,
            paths[0].Attribute("d")!.Value);
    }

    private static Rect ParseViewBox(string? value, string path)
    {
        var fields = value?.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        if (fields is not { Length: 4 } ||
            !double.TryParse(
                fields[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var x) ||
            !double.TryParse(
                fields[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var y) ||
            !double.TryParse(
                fields[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width) ||
            !double.TryParse(
                fields[3],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var height) ||
            !double.IsFinite(x) ||
            !double.IsFinite(y) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new InvalidDataException(
                $"The wrist icon has an invalid SVG viewBox: {path}");
        }

        return new Rect(x, y, width, height);
    }

    private sealed record IconAsset(
        string FileName,
        string Sha256,
        bool MirrorInRtl = false);

    private sealed record SvgDocument(Rect ViewBox, string PathData);

    private readonly record struct IconCacheKey(
        string SemanticId,
        int PixelSize,
        bool IsSelected,
        WristFlowDirection FlowDirection);

    private readonly record struct TextCacheKey(
        string AssetId,
        string Text,
        WristTextRole Role,
        double TextScale,
        int MaximumWidthPixels,
        WristFlowDirection FlowDirection);
}
