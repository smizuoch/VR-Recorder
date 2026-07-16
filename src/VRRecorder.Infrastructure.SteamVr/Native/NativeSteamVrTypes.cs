using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal enum NativeSteamVrStatus
{
    Ok = 0,
    InvalidArgument = 1,
    UnsupportedAbi = 2,
    InvalidState = 3,
    BackendUnavailable = 4,
    OutOfMemory = 5,
    InternalError = 6,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrInputConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint ActionManifestPathUtf8;
    public nint ActionSetPathUtf8;
    public nint DigitalActionPathUtf8;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrDigitalStateV1
{
    public uint StructSize;
    public uint AbiVersion;
    public byte IsActive;
    public byte State;
    public byte Changed;
    public byte Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrOverlayConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint ApplicationManifestPathUtf8;
    public nint OverlayKeyUtf8;
    public nint OverlayNameUtf8;
    public float WidthInMeters;
    public uint ReservedV1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrOverlayBgraFrameV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint PixelBytes;
    public ulong PixelBytesSize;
    public uint Width;
    public uint Height;
    public uint StrideBytes;
    public uint ReservedV1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrOverlayPointerEventV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint HasEvent;
    public uint Kind;
    public uint PixelX;
    public uint PixelY;
    public uint Button;
    public uint CursorIndex;
}
