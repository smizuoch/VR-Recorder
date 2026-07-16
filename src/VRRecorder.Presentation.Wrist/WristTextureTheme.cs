namespace VRRecorder.Presentation.Wrist;

public readonly record struct WristBgra32(byte B, byte G, byte R, byte A);

public sealed record WristTexturePalette(
    WristBgra32 Surface,
    WristBgra32 OnSurface,
    WristBgra32 SurfaceContainer,
    WristBgra32 Recording,
    WristBgra32 OnRecording,
    WristBgra32 Error,
    WristBgra32 OnError,
    WristBgra32 Selected,
    WristBgra32 OnSelected,
    WristBgra32 Disabled,
    WristBgra32 OnDisabled);

public sealed record WristTextureMetrics(
    int StateCornerRadiusPixels,
    int ActionCornerRadiusPixels,
    int ContentPaddingPixels,
    int ContentGapPixels,
    int StateIconPixels,
    int PrimaryIconPixels,
    int SecondaryIconPixels);

public sealed record WristTextureTheme(
    WristTexturePalette Palette,
    WristTextureMetrics Metrics);

public sealed class WristTextureThemeSet
{
    public WristTextureThemeSet(
        WristTextureTheme normal,
        WristTextureTheme highContrast)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(highContrast);
        Normal = normal;
        HighContrast = highContrast;
    }

    public WristTextureTheme Normal { get; }

    public WristTextureTheme HighContrast { get; }

    public WristTextureTheme Resolve(bool highContrast) =>
        highContrast ? HighContrast : Normal;
}
