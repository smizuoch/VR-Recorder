using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public enum WristElementKind
{
    Background,
    StateCue,
    TelemetryPanel,
    PrimaryAction,
    SecondaryAction,
}

public sealed record WristLayoutElement(
    string ElementId,
    WristElementKind Kind,
    WristPixelRect Bounds,
    int ZIndex,
    bool IsEnabled,
    string? SemanticId,
    UiCommandId? Command,
    int MinimumTargetDp);

public sealed record WristHitTarget(
    string ElementId,
    string SemanticId,
    UiCommandId Command,
    WristElementKind Kind,
    WristPixelRect Bounds,
    int MinimumTargetDp,
    int ZIndex);

public sealed class WristTextureLayout
{
    private readonly IReadOnlyList<WristHitTarget> _hitTestOrder;

    internal WristTextureLayout(
        int pixelWidth,
        int pixelHeight,
        int pixelsPerDp,
        WristLayoutOptions options,
        IEnumerable<WristLayoutElement> elements,
        IEnumerable<WristHitTarget> hitTargets)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PixelsPerDp = pixelsPerDp;
        Options = options;
        Elements = Array.AsReadOnly(elements.ToArray());
        HitTargets = Array.AsReadOnly(hitTargets.ToArray());
        _hitTestOrder = Array.AsReadOnly(HitTargets
            .OrderByDescending(target => target.ZIndex)
            .ThenBy(target => target.ElementId, StringComparer.Ordinal)
            .ToArray());
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int PixelsPerDp { get; }

    public WristLayoutOptions Options { get; }

    public IReadOnlyList<WristLayoutElement> Elements { get; }

    public IReadOnlyList<WristHitTarget> HitTargets { get; }

    public WristHitTarget? HitTest(int x, int y)
    {
        if (x < 0 || x >= PixelWidth || y < 0 || y >= PixelHeight)
        {
            return null;
        }

        return _hitTestOrder.FirstOrDefault(target =>
            target.Bounds.Contains(x, y));
    }
}
