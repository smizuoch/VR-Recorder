using System.Runtime.InteropServices;
using System.Text;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Infrastructure.SteamVr.Native;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class NativeSteamVrOverlayLifecycle
    : IWristOverlayPlacementRuntime, IDisposable
{
    public const string StableOverlayKey =
        OpenVrApplicationManifest.StableAppKey + ".wrist";
    public const string StableOverlayName = "VR Recorder Wrist";
    public const float DefaultWidthInMeters = 0.22F;
    public const float MinimumWidthInMeters = 0.18F;
    public const float MaximumWidthInMeters = 0.32F;
    public const int TexturePixelWidth = 1024;
    public const int TexturePixelHeight = 512;
    public const int TextureStrideBytes = 4096;
    public const int TexturePixelBytesSize = 2_097_152;
    private const int MaximumDeviceProfileUtf8Bytes = 6_144;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly object _lifetimeGate = new();
    private readonly NativeSteamVrLibrary _library;
    private readonly NativeSteamVrOverlaySafeHandle _overlay;
    private bool _disposed;

    public NativeSteamVrOverlayLifecycle(
        string libraryPath,
        string installRoot,
        float widthInMeters = DefaultWidthInMeters)
    {
        if (!float.IsFinite(widthInMeters) ||
            widthInMeters < MinimumWidthInMeters ||
            widthInMeters > MaximumWidthInMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthInMeters),
                widthInMeters,
                "The SteamVR overlay width must be between 0.18 and 0.32 metres.");
        }

        var applicationManifest =
            OpenVrApplicationManifest.ResolveAndValidate(installRoot);
        _library = new NativeSteamVrLibrary(libraryPath);
        try
        {
            _overlay = CreateOverlay(
                _library,
                applicationManifest.ManifestPath,
                widthInMeters);
        }
        catch
        {
            _library.Dispose();
            throw;
        }
    }

    public void Show() => Operate(
        static (library, overlay) => library.ShowOverlay(overlay),
        "show");

    public void Hide() => Operate(
        static (library, overlay) => library.HideOverlay(overlay),
        "hide");

    public void UpdateBgraTexture(
        ReadOnlyMemory<byte> bgraPixels,
        int pixelWidth,
        int pixelHeight,
        int strideBytes)
    {
        if (pixelWidth != TexturePixelWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelWidth),
                pixelWidth,
                "The SteamVR overlay texture width must be 1024 pixels.");
        }

        if (pixelHeight != TexturePixelHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelHeight),
                pixelHeight,
                "The SteamVR overlay texture height must be 512 pixels.");
        }

        if (strideBytes != TextureStrideBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strideBytes),
                strideBytes,
                "The SteamVR overlay texture stride must be 4096 bytes.");
        }

        if (bgraPixels.Length != TexturePixelBytesSize)
        {
            throw new ArgumentException(
                "The SteamVR overlay BGRA buffer must contain 2097152 bytes.",
                nameof(bgraPixels));
        }

        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            byte[]? copiedPixels = null;
            if (!MemoryMarshal.TryGetArray(
                    bgraPixels,
                    out ArraySegment<byte> segment))
            {
                copiedPixels = bgraPixels.ToArray();
                segment = new ArraySegment<byte>(copiedPixels);
            }

            var pinnedPixels = GCHandle.Alloc(
                segment.Array!,
                GCHandleType.Pinned);
            try
            {
                var frame = new NativeSteamVrOverlayBgraFrameV1
                {
                    StructSize = checked((uint)Marshal.SizeOf<
                        NativeSteamVrOverlayBgraFrameV1>()),
                    AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
                    PixelBytes = pinnedPixels.AddrOfPinnedObject() +
                        segment.Offset,
                    PixelBytesSize = checked((ulong)segment.Count),
                    Width = TexturePixelWidth,
                    Height = TexturePixelHeight,
                    StrideBytes = TextureStrideBytes,
                };
                var status = _library.UpdateOverlayBgra(
                    _overlay.DangerousGetHandle(),
                    ref frame);
                if (status != NativeSteamVrStatus.Ok)
                {
                    throw Failure(status, "update texture");
                }
            }
            finally
            {
                pinnedPixels.Free();
                GC.KeepAlive(copiedPixels);
            }
        }
    }

    public void ClearTexture() => Operate(
        static (library, overlay) => library.ClearOverlayTexture(overlay),
        "clear texture");

    public void ApplyPlacement(
        VrHand hand,
        OverlayPlacementMode placementMode,
        OverlayTransform transform)
    {
        if (!Enum.IsDefined(hand))
        {
            throw new ArgumentOutOfRangeException(nameof(hand), hand, null);
        }
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementMode),
                placementMode,
                null);
        }
        var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(transform);
        var pose = CreateNativePose(hand, placementMode, matrix);
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var status = _library.SetOverlayPose(
                _overlay.DangerousGetHandle(),
                ref pose);
            if (status != NativeSteamVrStatus.Ok)
            {
                throw Failure(status, "set pose");
            }
        }
    }

    public SteamVrOverlayPoseReadback ReadPlacement()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pose = new NativeSteamVrOverlayPoseV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrOverlayPoseV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
            };
            var status = _library.GetOverlayPose(
                _overlay.DangerousGetHandle(),
                ref pose);
            if (status != NativeSteamVrStatus.Ok ||
                !TryMapPose(pose, out var readback))
            {
                throw Failure(
                    status == NativeSteamVrStatus.Ok
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "get pose");
            }
            return readback;
        }
    }

    public OpenVrMatrix34 ConvertPlacement(
        VrHand hand,
        OverlayPlacementMode placementMode)
    {
        if (!Enum.IsDefined(hand))
        {
            throw new ArgumentOutOfRangeException(nameof(hand), hand, null);
        }
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementMode),
                placementMode,
                null);
        }
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pose = new NativeSteamVrOverlayPoseV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrOverlayPoseV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
            };
            var status = _library.ConvertOverlayPose(
                _overlay.DangerousGetHandle(),
                placementMode == OverlayPlacementMode.WristDock ? 1u : 2u,
                hand == VrHand.Left ? 1u : 2u,
                ref pose);
            if (status != NativeSteamVrStatus.Ok ||
                !TryMapPose(pose, out var readback) ||
                readback.PlacementMode != placementMode ||
                (placementMode == OverlayPlacementMode.WristDock &&
                 readback.DockHand != hand))
            {
                throw Failure(
                    status == NativeSteamVrStatus.Ok
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "convert pose");
            }
            return readback.Transform;
        }
    }

    public VrDeviceProfile ReadDeviceProfile(VrHand hand)
    {
        if (!Enum.IsDefined(hand))
        {
            throw new ArgumentOutOfRangeException(nameof(hand), hand, null);
        }
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var profile = CreateNativeDeviceProfile(hand);
            var status = _library.GetOverlayDeviceProfile(
                _overlay.DangerousGetHandle(),
                ref profile,
                utf8Buffer: null,
                utf8Capacity: 0,
                out var requiredUtf8Size);
            if (status != NativeSteamVrStatus.BufferTooSmall ||
                requiredUtf8Size is 0 or > MaximumDeviceProfileUtf8Bytes)
            {
                throw Failure(
                    status == NativeSteamVrStatus.BufferTooSmall
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "get device profile size");
            }

            var utf8 = new byte[requiredUtf8Size];
            profile = CreateNativeDeviceProfile(hand);
            status = _library.GetOverlayDeviceProfile(
                _overlay.DangerousGetHandle(),
                ref profile,
                utf8,
                checked((uint)utf8.Length),
                out var secondRequiredUtf8Size);
            if (status != NativeSteamVrStatus.Ok ||
                secondRequiredUtf8Size != requiredUtf8Size ||
                !TryMapDeviceProfile(hand, profile, utf8, out var result))
            {
                throw Failure(
                    status == NativeSteamVrStatus.Ok
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "get device profile");
            }
            return result;
        }
    }

    WristOverlayPlacementReadback
        IWristOverlayPlacementRuntime.ReadPlacement()
    {
        var readback = ReadPlacement();
        return new WristOverlayPlacementReadback(
            readback.PlacementMode,
            readback.DockHand,
            readback.TrackingOrigin,
            readback.Transform);
    }

    public SteamVrOverlayPointerEvent? PollPointerEvent()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var nativeEvent = new NativeSteamVrOverlayPointerEventV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrOverlayPointerEventV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
            };
            var status = _library.PollOverlayPointerEvent(
                _overlay.DangerousGetHandle(),
                ref nativeEvent);
            if (status != NativeSteamVrStatus.Ok)
            {
                throw Failure(status, "poll pointer event");
            }
            if (nativeEvent.HasEvent == 0)
            {
                return null;
            }
            if (nativeEvent.HasEvent != 1 ||
                nativeEvent.PixelX >= TexturePixelWidth ||
                nativeEvent.PixelY >= TexturePixelHeight ||
                !TryMapPointerEvent(nativeEvent, out var pointerEvent))
            {
                throw Failure(
                    NativeSteamVrStatus.InternalError,
                    "poll pointer event");
            }
            return pointerEvent;
        }
    }

    public void Close() => Operate(
        static (library, overlay) => library.CloseOverlay(overlay),
        "close");

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _overlay.Dispose();
            }
            finally
            {
                _library.Dispose();
            }
        }
    }

    private static NativeSteamVrOverlaySafeHandle CreateOverlay(
        NativeSteamVrLibrary library,
        string applicationManifestPath,
        float widthInMeters)
    {
        var manifestPath = Marshal.StringToCoTaskMemUTF8(
            applicationManifestPath);
        var overlayKey = Marshal.StringToCoTaskMemUTF8(StableOverlayKey);
        var overlayName = Marshal.StringToCoTaskMemUTF8(StableOverlayName);
        try
        {
            var config = new NativeSteamVrOverlayConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrOverlayConfigV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
                ApplicationManifestPathUtf8 = manifestPath,
                OverlayKeyUtf8 = overlayKey,
                OverlayNameUtf8 = overlayName,
                WidthInMeters = widthInMeters,
            };
            var status = library.CreateOverlay(ref config, out var overlay);
            if (status != NativeSteamVrStatus.Ok || overlay == 0)
            {
                throw Failure(
                    status == NativeSteamVrStatus.Ok
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "create");
            }

            return new NativeSteamVrOverlaySafeHandle(overlay, library);
        }
        finally
        {
            Marshal.FreeCoTaskMem(overlayName);
            Marshal.FreeCoTaskMem(overlayKey);
            Marshal.FreeCoTaskMem(manifestPath);
        }
    }

    private void Operate(
        Func<NativeSteamVrLibrary, nint, NativeSteamVrStatus> operation,
        string operationName)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var status = operation(
                _library,
                _overlay.DangerousGetHandle());
            if (status != NativeSteamVrStatus.Ok)
            {
                throw Failure(status, operationName);
            }
        }
    }

    private static SteamVrOverlayException Failure(
        NativeSteamVrStatus status,
        string operation) => new((int)status, operation);

    private static bool TryMapPointerEvent(
        NativeSteamVrOverlayPointerEventV1 nativeEvent,
        out SteamVrOverlayPointerEvent pointerEvent)
    {
        pointerEvent = default;
        if (!Enum.IsDefined(typeof(SteamVrOverlayPointerEventKind),
                nativeEvent.Kind) ||
            !Enum.IsDefined(typeof(SteamVrOverlayPointerButton),
                nativeEvent.Button))
        {
            return false;
        }

        var kind = (SteamVrOverlayPointerEventKind)nativeEvent.Kind;
        var button = (SteamVrOverlayPointerButton)nativeEvent.Button;
        var validButton = kind == SteamVrOverlayPointerEventKind.Move
            ? button == SteamVrOverlayPointerButton.None
            : button != SteamVrOverlayPointerButton.None;
        if (!validButton)
        {
            return false;
        }

        pointerEvent = new SteamVrOverlayPointerEvent(
            kind,
            checked((int)nativeEvent.PixelX),
            checked((int)nativeEvent.PixelY),
            button,
            nativeEvent.CursorIndex);
        return true;
    }

    private static NativeSteamVrOverlayPoseV1 CreateNativePose(
        VrHand hand,
        OverlayPlacementMode placementMode,
        OpenVrMatrix34 matrix) =>
        new()
        {
            StructSize = checked((uint)Marshal.SizeOf<
                NativeSteamVrOverlayPoseV1>()),
            AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
            PlacementMode = placementMode == OverlayPlacementMode.WristDock
                ? 1u
                : 2u,
            Hand = placementMode == OverlayPlacementMode.WristDock
                ? hand == VrHand.Left ? 1u : 2u
                : 0u,
            TrackingOrigin = placementMode == OverlayPlacementMode.WorldPin
                ? 1u
                : 0u,
            M00 = matrix.M00,
            M01 = matrix.M01,
            M02 = matrix.M02,
            M03 = matrix.M03,
            M10 = matrix.M10,
            M11 = matrix.M11,
            M12 = matrix.M12,
            M13 = matrix.M13,
            M20 = matrix.M20,
            M21 = matrix.M21,
            M22 = matrix.M22,
            M23 = matrix.M23,
        };

    private static bool TryMapPose(
        NativeSteamVrOverlayPoseV1 pose,
        out SteamVrOverlayPoseReadback readback)
    {
        readback = null!;
        var matrix = new OpenVrMatrix34(
            pose.M00,
            pose.M01,
            pose.M02,
            pose.M03,
            pose.M10,
            pose.M11,
            pose.M12,
            pose.M13,
            pose.M20,
            pose.M21,
            pose.M22,
            pose.M23);
        if (pose.ReservedV1 != 0 ||
            matrix.ToArray().Any(value => !float.IsFinite(value)))
        {
            return false;
        }
        if (pose.PlacementMode == 1 &&
            pose.TrackingOrigin == 0 &&
            pose.Hand is 1 or 2)
        {
            readback = new SteamVrOverlayPoseReadback(
                OverlayPlacementMode.WristDock,
                pose.Hand == 1 ? VrHand.Left : VrHand.Right,
                TrackingOrigin: null,
                matrix);
            return true;
        }
        if (pose.PlacementMode == 2 &&
            pose.Hand == 0 &&
            pose.TrackingOrigin == 1)
        {
            readback = new SteamVrOverlayPoseReadback(
                OverlayPlacementMode.WorldPin,
                DockHand: null,
                WristOverlayTrackingOrigin.Standing,
                matrix);
            return true;
        }
        return false;
    }

    private static NativeSteamVrDeviceProfileV1 CreateNativeDeviceProfile(
        VrHand hand) =>
        new()
        {
            StructSize = checked((uint)Marshal.SizeOf<
                NativeSteamVrDeviceProfileV1>()),
            AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
            Hand = hand == VrHand.Left ? 1u : 2u,
        };

    private static bool TryMapDeviceProfile(
        VrHand hand,
        NativeSteamVrDeviceProfileV1 profile,
        byte[] utf8,
        out VrDeviceProfile result)
    {
        result = null!;
        var expectedHand = hand == VrHand.Left ? 1u : 2u;
        var trackingEnd =
            (ulong)profile.TrackingSystemNameOffset +
            profile.TrackingSystemNameSize;
        var hmdEnd =
            (ulong)profile.HmdModelNumberOffset +
            profile.HmdModelNumberSize;
        var controllerEnd =
            (ulong)profile.ControllerInputProfilePathOffset +
            profile.ControllerInputProfilePathSize;
        if (profile.ReservedV1 != 0 || profile.Hand != expectedHand ||
            profile.TrackingSystemNameOffset != 0 ||
            profile.HmdModelNumberOffset != trackingEnd ||
            profile.ControllerInputProfilePathOffset != hmdEnd ||
            controllerEnd != (ulong)utf8.Length)
        {
            return false;
        }
        try
        {
            var trackingSystem = DecodeProfileText(
                utf8,
                profile.TrackingSystemNameOffset,
                profile.TrackingSystemNameSize);
            var hmdModel = DecodeProfileText(
                utf8,
                profile.HmdModelNumberOffset,
                profile.HmdModelNumberSize);
            var controllerProfile = DecodeProfileText(
                utf8,
                profile.ControllerInputProfilePathOffset,
                profile.ControllerInputProfilePathSize);
            result = new VrDeviceProfile(
                trackingSystem,
                hmdModel,
                controllerProfile);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static string DecodeProfileText(
        byte[] utf8,
        uint offset,
        uint size)
    {
        var end = (ulong)offset + size;
        if (size == 0 || end > (ulong)utf8.Length)
        {
            throw new ArgumentException("Invalid device profile UTF-8 range.");
        }
        var value = StrictUtf8.GetString(
            utf8,
            checked((int)offset),
            checked((int)size));
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            throw new ArgumentException("Invalid device profile identity.");
        }
        return value;
    }
}
