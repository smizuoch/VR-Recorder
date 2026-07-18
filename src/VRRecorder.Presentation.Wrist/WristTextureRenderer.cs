using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristTextureRenderer
{
    private readonly IWristRasterAssetProvider _assets;
    private readonly WristTextureThemeSet _themes;

    public WristTextureRenderer(
        IWristRasterAssetProvider assets,
        WristTextureThemeSet themes)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(themes);
        _assets = assets;
        _themes = themes;
        ValidateTheme(themes.Normal);
        ValidateTheme(themes.HighContrast);
    }

    public WristTextureFrame Render(
        WristUiSnapshot snapshot,
        WristLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        var layout = WristTextureLayoutEngine.Layout(snapshot, options);
        var theme = _themes.Resolve(options.HighContrast);
        var pixels = new byte[checked(
            layout.PixelWidth * layout.PixelHeight * 4)];
        var surface = new BgraSurface(
            layout.PixelWidth,
            layout.PixelHeight,
            pixels);
        surface.Fill(theme.Palette.Surface);
        var actions = snapshot.Actions.ToDictionary(
            action => action.SemanticId,
            StringComparer.Ordinal);
        foreach (var element in layout.Elements)
        {
            switch (element.Kind)
            {
                case WristElementKind.Background:
                    break;
                case WristElementKind.StateCue:
                    DrawStateCue(
                        surface,
                        snapshot,
                        element,
                        theme,
                        options);
                    break;
                case WristElementKind.TelemetryPanel:
                    DrawTelemetryPanel(
                        surface,
                        snapshot.Telemetry ??
                            throw new InvalidOperationException(
                                "A telemetry panel requires telemetry."),
                        element,
                        theme,
                        options);
                    break;
                case WristElementKind.PrimaryAction:
                case WristElementKind.SecondaryAction:
                    DrawAction(
                        surface,
                        actions[RequireSemanticId(element)],
                        element,
                        theme,
                        options);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The wrist layout contains an unknown element kind.");
            }
        }

        return new WristTextureFrame(snapshot.Revision, layout, pixels);
    }

    private void DrawTelemetryPanel(
        BgraSurface surface,
        WristTelemetrySnapshot telemetry,
        WristLayoutElement element,
        WristTextureTheme theme,
        WristLayoutOptions options)
    {
        surface.FillRoundedRectangle(
            element.Bounds,
            theme.Metrics.StateCornerRadiusPixels,
            theme.Palette.SurfaceContainer);
        var lines = element.ElementId switch
        {
            "telemetry:recording" => new (string AssetId, string Text)[]
            {
                ("telemetry.elapsed", telemetry.ElapsedText),
                ("telemetry.resolution", telemetry.ResolutionText),
                ("telemetry.fps", telemetry.FramesPerSecondText),
                ("telemetry.encoder", telemetry.EncoderDisplayName),
            },
            "telemetry:health" => CreateTelemetryHealthLines(telemetry),
            _ => throw new InvalidOperationException(
                "The telemetry panel identity is invalid."),
        };
        DrawTelemetryLines(
            surface,
            element.Bounds,
            lines,
            theme.Palette.OnSurface,
            options);
    }

    private static (string AssetId, string Text)[]
        CreateTelemetryHealthLines(WristTelemetrySnapshot telemetry)
    {
        var lines = new List<(string AssetId, string Text)>
        {
            ("telemetry.spout", $"Spout: {HealthText(telemetry.SpoutSignal)}"),
            ("telemetry.desktop-audio",
                $"Desktop: {HealthText(telemetry.DesktopAudioSignal)}"),
            ("telemetry.microphone",
                $"Microphone: {HealthText(telemetry.MicrophoneSignal)}"),
            ("telemetry.placement", $"Placement: {telemetry.PlacementMode}"),
        };
        var alert = telemetry.Alerts
            .OrderByDescending(value => value.Severity)
            .FirstOrDefault();
        if (alert is not null)
        {
            lines.Add((alert.Message.ResourceKey, alert.Message.Value));
        }
        return [.. lines];
    }

    private void DrawTelemetryLines(
        BgraSurface surface,
        WristPixelRect bounds,
        IReadOnlyList<(string AssetId, string Text)> lines,
        WristBgra32 foreground,
        WristLayoutOptions options)
    {
        const int padding = 8;
        const int gap = 2;
        var maximumWidth = checked(bounds.Width - padding * 2);
        var masks = lines
            .Select(line => ResolveText(new WristTextRasterRequest(
                line.AssetId,
                line.Text,
                WristTextRole.SecondaryAction,
                options.TextScale,
                maximumWidth,
                options.FlowDirection)))
            .ToArray();
        var contentHeight = checked(
            masks.Sum(mask => mask.Height) + gap * (masks.Length - 1));
        if (contentHeight > bounds.Height - padding * 2)
        {
            throw new InvalidOperationException(
                "The wrist telemetry does not fit its layout bounds.");
        }
        var top = bounds.Top + (bounds.Height - contentHeight) / 2;
        for (var index = 0; index < masks.Length; index++)
        {
            var left = options.FlowDirection == WristFlowDirection.RightToLeft
                ? bounds.Right - padding - masks[index].Width
                : bounds.Left + padding;
            surface.BlendMask(left, top, masks[index], foreground);
            top = checked(top + masks[index].Height + gap);
        }
    }

    private static string HealthText(WristSignalHealth health) => health switch
    {
        WristSignalHealth.NotApplicable => "N/A",
        WristSignalHealth.Available => "Available",
        WristSignalHealth.Degraded => "Degraded",
        WristSignalHealth.Unavailable => "Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(health)),
    };

    private void DrawStateCue(
        BgraSurface surface,
        WristUiSnapshot snapshot,
        WristLayoutElement element,
        WristTextureTheme theme,
        WristLayoutOptions options)
    {
        var (background, foreground) = ResolveColors(
            snapshot.StateCue.ColorRole,
            isEnabled: true,
            isSelected: false,
            theme.Palette);
        surface.FillRoundedRectangle(
            element.Bounds,
            theme.Metrics.StateCornerRadiusPixels,
            background);
        var icon = ResolveIcon(new WristIconRasterRequest(
            snapshot.StateCue.IconSemanticId,
            theme.Metrics.StateIconPixels,
            IsSelected: false,
            options.FlowDirection));
        var maximumTextWidth = checked(
            element.Bounds.Width -
            theme.Metrics.ContentPaddingPixels * 3 -
            icon.Width);
        var text = ResolveText(new WristTextRasterRequest(
            snapshot.StateCue.Label.ResourceKey,
            snapshot.StateCue.Label.Value,
            WristTextRole.State,
            options.TextScale,
            maximumTextWidth,
            options.FlowDirection));
        var contentWidth = checked(
            icon.Width + theme.Metrics.ContentGapPixels + text.Width);
        var left = options.FlowDirection == WristFlowDirection.RightToLeft
            ? element.Bounds.Right -
              theme.Metrics.ContentPaddingPixels - contentWidth
            : element.Bounds.Left + theme.Metrics.ContentPaddingPixels;
        var iconLeft = options.FlowDirection == WristFlowDirection.RightToLeft
            ? left + text.Width + theme.Metrics.ContentGapPixels
            : left;
        var textLeft = options.FlowDirection == WristFlowDirection.RightToLeft
            ? left
            : left + icon.Width + theme.Metrics.ContentGapPixels;
        surface.BlendMask(
            iconLeft,
            CenterY(element.Bounds, icon.Height),
            icon,
            foreground);
        surface.BlendMask(
            textLeft,
            CenterY(element.Bounds, text.Height),
            text,
            foreground);
    }

    private void DrawAction(
        BgraSurface surface,
        UiActionSnapshot action,
        WristLayoutElement element,
        WristTextureTheme theme,
        WristLayoutOptions options)
    {
        var (background, foreground) = ResolveColors(
            action.ColorRole,
            action.IsEnabled,
            action.IsSelected,
            theme.Palette);
        surface.FillRoundedRectangle(
            element.Bounds,
            theme.Metrics.ActionCornerRadiusPixels,
            background);
        var isPrimary = element.Kind == WristElementKind.PrimaryAction;
        var iconSize = isPrimary
            ? theme.Metrics.PrimaryIconPixels
            : theme.Metrics.SecondaryIconPixels;
        var icon = ResolveIcon(new WristIconRasterRequest(
            action.IconSemanticId,
            iconSize,
            action.IsSelected,
            options.FlowDirection));
        var role = isPrimary
            ? WristTextRole.PrimaryAction
            : WristTextRole.SecondaryAction;
        var maximumTextWidth = isPrimary
            ? checked(element.Bounds.Width -
                      theme.Metrics.ContentPaddingPixels * 2)
            : checked(element.Bounds.Width -
                      theme.Metrics.ContentPaddingPixels * 2 -
                      icon.Width -
                      theme.Metrics.ContentGapPixels);
        var text = ResolveText(new WristTextRasterRequest(
            action.VisibleLabel.ResourceKey,
            action.VisibleLabel.Value,
            role,
            options.TextScale,
            maximumTextWidth,
            options.FlowDirection));
        if (isPrimary)
        {
            DrawVerticalContent(
                surface,
                element.Bounds,
                icon,
                text,
                foreground,
                theme.Metrics.ContentGapPixels);
        }
        else
        {
            DrawHorizontalContent(
                surface,
                element.Bounds,
                icon,
                text,
                foreground,
                theme.Metrics.ContentGapPixels,
                options.FlowDirection);
        }
    }

    private static void DrawVerticalContent(
        BgraSurface surface,
        WristPixelRect bounds,
        WristAlphaMask icon,
        WristAlphaMask text,
        WristBgra32 color,
        int gap)
    {
        var contentHeight = checked(icon.Height + gap + text.Height);
        if (contentHeight > bounds.Height)
        {
            throw new InvalidOperationException(
                "The primary wrist content does not fit its layout bounds.");
        }

        var top = bounds.Top + (bounds.Height - contentHeight) / 2;
        surface.BlendMask(CenterX(bounds, icon.Width), top, icon, color);
        surface.BlendMask(
            CenterX(bounds, text.Width),
            top + icon.Height + gap,
            text,
            color);
    }

    private static void DrawHorizontalContent(
        BgraSurface surface,
        WristPixelRect bounds,
        WristAlphaMask icon,
        WristAlphaMask text,
        WristBgra32 color,
        int gap,
        WristFlowDirection direction)
    {
        var contentWidth = checked(icon.Width + gap + text.Width);
        if (contentWidth > bounds.Width)
        {
            throw new InvalidOperationException(
                "The secondary wrist content does not fit its layout bounds.");
        }

        var left = bounds.Left + (bounds.Width - contentWidth) / 2;
        var iconLeft = direction == WristFlowDirection.RightToLeft
            ? left + text.Width + gap
            : left;
        var textLeft = direction == WristFlowDirection.RightToLeft
            ? left
            : left + icon.Width + gap;
        surface.BlendMask(
            iconLeft,
            CenterY(bounds, icon.Height),
            icon,
            color);
        surface.BlendMask(
            textLeft,
            CenterY(bounds, text.Height),
            text,
            color);
    }

    private WristAlphaMask ResolveIcon(WristIconRasterRequest request)
    {
        if (!_assets.TryRasterizeIcon(request, out var mask) || mask is null)
        {
            throw new WristRasterAssetMissingException(request.SemanticId);
        }

        if (mask.Width > request.PixelSize || mask.Height > request.PixelSize)
        {
            throw new InvalidOperationException(
                "The wrist icon mask exceeds its requested size.");
        }
        return mask;
    }

    private WristAlphaMask ResolveText(WristTextRasterRequest request)
    {
        if (!_assets.TryRasterizeText(request, out var mask) || mask is null)
        {
            throw new WristRasterAssetMissingException(request.AssetId);
        }

        if (mask.Width > request.MaximumWidthPixels)
        {
            throw new InvalidOperationException(
                "The wrist text mask exceeds its requested width.");
        }
        return mask;
    }

    private static (WristBgra32 Background, WristBgra32 Foreground)
        ResolveColors(
            UiColorRole role,
            bool isEnabled,
            bool isSelected,
            WristTexturePalette palette)
    {
        if (!isEnabled)
        {
            return (palette.Disabled, palette.OnDisabled);
        }
        if (isSelected)
        {
            return (palette.Selected, palette.OnSelected);
        }
        return role switch
        {
            UiColorRole.Recording =>
                (palette.Recording, palette.OnRecording),
            UiColorRole.Error => (palette.Error, palette.OnError),
            UiColorRole.Surface =>
                (palette.SurfaceContainer, palette.OnSurface),
            _ => throw new InvalidOperationException(
                "The wrist element uses an unknown color role."),
        };
    }

    private static string RequireSemanticId(WristLayoutElement element) =>
        element.SemanticId ?? throw new InvalidOperationException(
            "The wrist action layout is missing its semantic ID.");

    private static int CenterX(WristPixelRect bounds, int width) =>
        bounds.Left + (bounds.Width - width) / 2;

    private static int CenterY(WristPixelRect bounds, int height) =>
        bounds.Top + (bounds.Height - height) / 2;

    private static void ValidateTheme(WristTextureTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(theme.Palette);
        ArgumentNullException.ThrowIfNull(theme.Metrics);
        WristBgra32[] colors =
        [
            theme.Palette.Surface,
            theme.Palette.OnSurface,
            theme.Palette.SurfaceContainer,
            theme.Palette.Recording,
            theme.Palette.OnRecording,
            theme.Palette.Error,
            theme.Palette.OnError,
            theme.Palette.Selected,
            theme.Palette.OnSelected,
            theme.Palette.Disabled,
            theme.Palette.OnDisabled,
        ];
        if (colors.Any(color => color.A != byte.MaxValue))
        {
            throw new ArgumentException(
                "Resolved wrist theme colors must be opaque.",
                nameof(theme));
        }

        var metrics = theme.Metrics;
        if (metrics.StateCornerRadiusPixels < 0 ||
            metrics.ActionCornerRadiusPixels < 0 ||
            metrics.ContentPaddingPixels < 0 ||
            metrics.ContentGapPixels < 0 ||
            metrics.StateIconPixels < 1 ||
            metrics.PrimaryIconPixels < 1 ||
            metrics.SecondaryIconPixels < 1)
        {
            throw new ArgumentException(
                "Resolved wrist theme metrics are invalid.",
                nameof(theme));
        }
    }

    private sealed class BgraSurface(
        int width,
        int height,
        byte[] pixels)
    {
        public void Fill(WristBgra32 color)
        {
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }
        }

        public void FillRoundedRectangle(
            WristPixelRect bounds,
            int requestedRadius,
            WristBgra32 color)
        {
            EnsureInside(bounds);
            var radius = Math.Min(
                requestedRadius,
                Math.Min(bounds.Width, bounds.Height) / 2);
            for (var y = 0; y < bounds.Height; y++)
            {
                for (var x = 0; x < bounds.Width; x++)
                {
                    if (IsInsideRoundedRectangle(
                            x,
                            y,
                            bounds.Width,
                            bounds.Height,
                            radius))
                    {
                        SetOpaque(bounds.Left + x, bounds.Top + y, color);
                    }
                }
            }
        }

        public void BlendMask(
            int left,
            int top,
            WristAlphaMask mask,
            WristBgra32 color)
        {
            var bounds = new WristPixelRect(
                left,
                top,
                mask.Width,
                mask.Height);
            EnsureInside(bounds);
            var alpha = mask.Alpha.Span;
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    Blend(
                        left + x,
                        top + y,
                        color,
                        alpha[y * mask.Width + x]);
                }
            }
        }

        private static bool IsInsideRoundedRectangle(
            int x,
            int y,
            int rectangleWidth,
            int rectangleHeight,
            int radius)
        {
            if (radius == 0 ||
                (x >= radius && x < rectangleWidth - radius) ||
                (y >= radius && y < rectangleHeight - radius))
            {
                return true;
            }

            var centerX = x < radius ? radius : rectangleWidth - radius - 1;
            var centerY = y < radius ? radius : rectangleHeight - radius - 1;
            var deltaX = x - centerX;
            var deltaY = y - centerY;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

        private void Blend(
            int x,
            int y,
            WristBgra32 color,
            byte alpha)
        {
            if (alpha == 0)
            {
                return;
            }

            var index = checked((y * width + x) * 4);
            var inverse = byte.MaxValue - alpha;
            pixels[index] = Composite(color.B, pixels[index], alpha, inverse);
            pixels[index + 1] = Composite(
                color.G,
                pixels[index + 1],
                alpha,
                inverse);
            pixels[index + 2] = Composite(
                color.R,
                pixels[index + 2],
                alpha,
                inverse);
            pixels[index + 3] = byte.MaxValue;
        }

        private static byte Composite(
            byte source,
            byte destination,
            int alpha,
            int inverse) => checked((byte)(
            (source * alpha + destination * inverse + 127) /
            byte.MaxValue));

        private void SetOpaque(int x, int y, WristBgra32 color)
        {
            var index = checked((y * width + x) * 4);
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = byte.MaxValue;
        }

        private void EnsureInside(WristPixelRect bounds)
        {
            if (bounds.Right > width || bounds.Bottom > height)
            {
                throw new InvalidOperationException(
                    "The wrist raster content exceeds the texture bounds.");
            }
        }
    }
}
