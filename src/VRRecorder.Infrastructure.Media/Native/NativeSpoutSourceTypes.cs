using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal enum NativeGpuVendor : uint
{
    Unknown = 0,
    Nvidia = 1,
    Amd = 2,
    Intel = 3,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSpoutSourceConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint ReservedV1;
    public uint ReservedV2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSpoutSenderSnapshotV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint SenderIdOffset;
    public uint SenderIdSize;
    public ulong LatestFrameGeneration;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSpoutFrameV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint SenderIdOffset;
    public uint SenderIdSize;
    public uint GpuIdentityOffset;
    public uint GpuIdentitySize;
    public ulong AdapterLuid;
    public NativeGpuVendor GpuVendor;
    public uint Width;
    public uint Height;
    public NativeSourcePixelFormat PixelFormat;
    public double EstimatedSourceFramesPerSecond;
    public ulong FrameSequence;
    public long MonotonicTimestampMicroseconds;
    public ulong Reserved;
}
