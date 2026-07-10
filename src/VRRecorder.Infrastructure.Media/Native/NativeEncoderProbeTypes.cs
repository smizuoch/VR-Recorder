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
