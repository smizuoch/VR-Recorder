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
