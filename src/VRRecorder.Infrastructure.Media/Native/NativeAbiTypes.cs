using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    UnsupportedAbi = 2,
    InvalidState = 3,
    BackendUnavailable = 4,
    OutOfMemory = 5,
    InternalError = 6,
    BufferTooSmall = 7,
    Timeout = 8,
}

internal enum NativeEventKind : uint
{
    FirstVideoPacketMuxed = 1,
    Stopped = 2,
    Faulted = 3,
    DesktopAudioDeviceLost = 4,
    DesktopAudioDeviceRecovered = 5,
    MicrophoneAudioDeviceLost = 6,
    MicrophoneAudioDeviceRecovered = 7,
    AudioVideoDriftExceeded = 8,
}

internal enum NativeEncoderKind : uint
{
    Nvenc = 1,
    Amf = 2,
    Qsv = 3,
    MediaFoundationSoftware = 4,
}

internal enum NativeCanvasBackground : uint
{
    Black = 1,
}

internal enum NativeVideoRotation : uint
{
    None = 1,
}

internal enum NativeSourcePixelFormat : uint
{
    Bgra8 = 1,
    Rgba8 = 2,
    Nv12 = 3,
}

internal enum NativeAudioRouting : uint
{
    Mixed = 1,
    DesktopOnly = 2,
    MicOnly = 3,
    Muted = 4,
}

internal enum NativeQualityPreset : uint
{
    Standard = 1,
    High = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSessionConfigV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint TemporaryOutputPathUtf8;
    public uint Width;
    public uint Height;
    public uint FramesPerSecondNumerator;
    public uint FramesPerSecondDenominator;
    public long StartedAtUnixMillisecondsUtc;
    public NativeEncoderKind Encoder;
    public uint Reserved;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint DestinationX;
    public uint DestinationY;
    public uint DestinationWidth;
    public uint DestinationHeight;
    public NativeCanvasBackground CanvasBackground;
    public NativeVideoRotation Rotation;
    public NativeAudioRouting AudioRouting;
    public NativeQualityPreset QualityPreset;
    public nint DesktopEndpointIdUtf8;
    public nint MicrophoneEndpointIdUtf8;
    public double DesktopGainDb;
    public double MicrophoneGainDb;
    public nint SpoutSenderIdentityUtf8;
    public ulong SpoutAdapterLuid;
    public ulong EncoderAdapterLuid;
    public nint GpuIdentityUtf8;
    public ulong ReservedV1;
    public NativeSourcePixelFormat SourcePixelFormat;
    public uint ReservedV2;
    public double EstimatedSourceFramesPerSecond;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVideoLayoutV1
{
    public uint StructSize;
    public uint AbiVersion;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint CanvasWidth;
    public uint CanvasHeight;
    public uint DestinationX;
    public uint DestinationY;
    public uint DestinationWidth;
    public uint DestinationHeight;
    public NativeCanvasBackground CanvasBackground;
    public NativeVideoRotation Rotation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAudioRoutingUpdateV1
{
    public uint StructSize;
    public uint AbiVersion;
    public NativeAudioRouting AudioRouting;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSessionStatisticsV1
{
    public uint StructSize;
    public uint AbiVersion;
    public ulong SourceVideoFrameCount;
    public ulong MuxedVideoPacketCount;
    public ulong MuxedAudioPacketCount;
    public ulong DroppedSourceVideoFrameCount;
    public ulong DuplicatedOutputVideoFrameCount;
    public ulong LatestEncodeLatencyMicroseconds;
    public ulong MaximumEncodeLatencyMicroseconds;
    public long AudioVideoOffsetMicroseconds;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEventV1
{
    public uint StructSize;
    public uint AbiVersion;
    public NativeEventKind Kind;
    public NativeStatus Status;
    public ulong Sequence;
    public ulong VideoPacketCount;
    public ulong AudioPacketCount;
    public nint MessageUtf8;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCallbacksV1
{
    public uint StructSize;
    public uint AbiVersion;
    public nint OnEvent;
    public nint UserData;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void NativeEventCallback(nint userData, nint nativeEvent);
