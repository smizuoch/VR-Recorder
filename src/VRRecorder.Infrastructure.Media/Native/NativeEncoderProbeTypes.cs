using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEncoderProbeConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public NativeEncoderKind EncoderKind;
    public uint SyntheticFrameCount;
    public ulong AdapterLuid;
    public uint Width;
    public uint Height;
    public uint FramesPerSecondNumerator;
    public uint FramesPerSecondDenominator;
    public nint GpuIdentityUtf8;
    public ulong Reserved;
}

internal enum NativeEncoderInputFormat : uint
{
    SystemMemoryNv12 = 1,
    D3d11Nv12 = 2,
    QsvNv12 = 3,
}

[Flags]
internal enum NativeEncoderProbeValidation : uint
{
    None = 0,
    NonemptyPacket = 0x0001,
    ParseableAccessUnit = 0x0002,
    Sps = 0x0004,
    Pps = 0x0008,
    Idr = 0x0010,
    DisplayDimensions = 0x0020,
    Profile = 0x0040,
    FrameRate = 0x0080,
    ZeroBFrames = 0x0100,
    Decoded = 0x0200,
    SameAdapter = 0x0400,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEncoderProbeResultV2
{
    public uint StructSize;
    public uint AbiVersion;
    public NativeEncoderKind ActualEncoderKind;
    public uint HardwareAccelerated;
    public ulong AdapterLuid;
    public NativeEncoderInputFormat OpenedInputFormat;
    public uint Width;
    public uint Height;
    public uint FramesPerSecondNumerator;
    public uint FramesPerSecondDenominator;
    public NativeEncoderProbeValidation ValidationFlags;
    public uint CodecNameOffset;
    public uint CodecNameSize;
    public uint DriverIdentityOffset;
    public uint DriverIdentitySize;
    public uint FfmpegBuildIdentityOffset;
    public uint FfmpegBuildIdentitySize;
    public uint ProfileOffset;
    public uint ProfileSize;
    public uint DeviceIdentityOffset;
    public uint DeviceIdentitySize;
    public ulong Reserved;
}
