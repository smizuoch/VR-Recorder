namespace VRRecorder.Presentation.Wrist;

public enum WristTextRole
{
    State,
    PrimaryAction,
    SecondaryAction,
}

public sealed record WristIconRasterRequest(
    string SemanticId,
    int PixelSize,
    bool IsSelected,
    WristFlowDirection FlowDirection);

public sealed record WristTextRasterRequest(
    string AssetId,
    string Text,
    WristTextRole Role,
    double TextScale,
    int MaximumWidthPixels,
    WristFlowDirection FlowDirection);

public sealed class WristAlphaMask
{
    private readonly byte[] _alpha;

    public WristAlphaMask(
        int width,
        int height,
        IEnumerable<byte> alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(alpha);
        _alpha = alpha.ToArray();
        if (_alpha.Length != checked(width * height))
        {
            throw new ArgumentException(
                "The alpha mask length does not match its dimensions.",
                nameof(alpha));
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Alpha => _alpha;
}

public interface IWristRasterAssetProvider
{
    bool TryRasterizeIcon(
        WristIconRasterRequest request,
        out WristAlphaMask? mask);

    bool TryRasterizeText(
        WristTextRasterRequest request,
        out WristAlphaMask? mask);
}

public sealed class WristRasterAssetMissingException : Exception
{
    public WristRasterAssetMissingException(string assetId)
        : base($"The wrist raster asset is unavailable: {assetId}.")
    {
        AssetId = assetId;
    }

    public string AssetId { get; }
}
