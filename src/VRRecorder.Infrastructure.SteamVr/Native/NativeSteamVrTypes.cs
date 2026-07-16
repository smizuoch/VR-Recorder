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
    BufferTooSmall = 7,
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
internal struct NativeSteamVrHapticConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint ActionManifestPathUtf8;
    public nint HapticActionPathUtf8;
    public nint InputSourcePathUtf8;
    public uint ReservedV1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrHapticPulseV1
{
    public uint StructSize;
    public uint AbiVersion;
    public float DurationSeconds;
    public float FrequencyHertz;
    public float Amplitude;
    public uint ReservedV1;
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

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrOverlayPoseV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint PlacementMode;
    public uint Hand;
    public uint TrackingOrigin;
    public uint ReservedV1;
    public float M00;
    public float M01;
    public float M02;
    public float M03;
    public float M10;
    public float M11;
    public float M12;
    public float M13;
    public float M20;
    public float M21;
    public float M22;
    public float M23;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSteamVrDeviceProfileV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint Hand;
    public uint ReservedV1;
    public uint TrackingSystemNameOffset;
    public uint TrackingSystemNameSize;
    public uint HmdModelNumberOffset;
    public uint HmdModelNumberSize;
    public uint ControllerInputProfilePathOffset;
    public uint ControllerInputProfilePathSize;
}
