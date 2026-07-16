namespace VRRecorder.Presentation.Wrist.Windows;

public static class WindowsWristTextureThemes
{
    private static readonly WristTextureMetrics Metrics = new(
        StateCornerRadiusPixels: 20,
        ActionCornerRadiusPixels: 28,
        ContentPaddingPixels: 20,
        ContentGapPixels: 12,
        StateIconPixels: 48,
        PrimaryIconPixels: 72,
        SecondaryIconPixels: 36);

    public static WristTextureThemeSet Default { get; } = new(
        new WristTextureTheme(
            new WristTexturePalette(
                Surface: Rgb(0x14, 0x12, 0x18),
                OnSurface: Rgb(0xE6, 0xE1, 0xE5),
                SurfaceContainer: Rgb(0x2B, 0x29, 0x30),
                Recording: Rgb(0xFF, 0xB4, 0xAB),
                OnRecording: Rgb(0x69, 0x00, 0x05),
                Error: Rgb(0xFF, 0xB4, 0xAB),
                OnError: Rgb(0x69, 0x00, 0x05),
                Selected: Rgb(0xD0, 0xBC, 0xFF),
                OnSelected: Rgb(0x38, 0x1E, 0x72),
                Disabled: Rgb(0x49, 0x45, 0x4F),
                OnDisabled: Rgb(0xCA, 0xC4, 0xD0)),
            Metrics),
        new WristTextureTheme(
            new WristTexturePalette(
                Surface: Rgb(0x00, 0x00, 0x00),
                OnSurface: Rgb(0xFF, 0xFF, 0xFF),
                SurfaceContainer: Rgb(0x20, 0x20, 0x20),
                Recording: Rgb(0xFF, 0x54, 0x49),
                OnRecording: Rgb(0x00, 0x00, 0x00),
                Error: Rgb(0xFF, 0x54, 0x49),
                OnError: Rgb(0x00, 0x00, 0x00),
                Selected: Rgb(0xFF, 0xFF, 0xFF),
                OnSelected: Rgb(0x00, 0x00, 0x00),
                Disabled: Rgb(0x5A, 0x5A, 0x5A),
                OnDisabled: Rgb(0xFF, 0xFF, 0xFF)),
            Metrics));

    private static WristBgra32 Rgb(byte red, byte green, byte blue) =>
        new(blue, green, red, byte.MaxValue);
}
