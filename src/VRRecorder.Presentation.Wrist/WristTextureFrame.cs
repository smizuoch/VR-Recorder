using System.Security.Cryptography;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristTextureFrame
{
    private readonly byte[] _bgraPixels;

    internal WristTextureFrame(
        long revision,
        WristTextureLayout layout,
        byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bgraPixels);
        var expectedLength = checked(
            layout.PixelWidth * layout.PixelHeight * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                "The BGRA buffer length does not match the wrist layout.",
                nameof(bgraPixels));
        }

        Revision = revision;
        Layout = layout;
        PixelWidth = layout.PixelWidth;
        PixelHeight = layout.PixelHeight;
        StrideBytes = checked(PixelWidth * 4);
        _bgraPixels = bgraPixels;
        Sha256Hex = Convert.ToHexString(SHA256.HashData(_bgraPixels));
    }

    public long Revision { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int StrideBytes { get; }

    public WristTextureLayout Layout { get; }

    public ReadOnlyMemory<byte> BgraPixels => _bgraPixels;

    public string Sha256Hex { get; }
}
