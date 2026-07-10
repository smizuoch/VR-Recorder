using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.Infrastructure.Media;

public sealed class PInvokeNativeRecordingBackend
    : INativeRecordingBackend, IDisposable
{
    private static readonly NativeEventCallback EventCallback = OnNativeEvent;
    private readonly object _lifetimeGate = new();
    private readonly NativeAbiLibrary _library;
    private int _activeSessionCount;
    private bool _disposed;

    public PInvokeNativeRecordingBackend(string libraryPath)
    {
        _library = new NativeAbiLibrary(libraryPath);
    }

    public Task<INativeRecordingSession> OpenAsync(
        RecordingPlan plan,
        NativeRecordingCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(callbacks);
        cancellationToken.ThrowIfCancellationRequested();

        AcquireSessionLease();
        try
        {
            return OpenCore(plan, callbacks);
        }
        catch
        {
            ReleaseSessionLease();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            if (_activeSessionCount != 0)
            {
                throw new InvalidOperationException(
                    "The native recording backend has an active session.");
            }

            _library.Dispose();
            _disposed = true;
        }
    }

    private Task<INativeRecordingSession> OpenCore(
        RecordingPlan plan,
        NativeRecordingCallbacks callbacks)
    {
        var callbackState = new NativeCallbackState(plan, callbacks);
        var callbackHandle = GCHandle.Alloc(callbackState);
        var nativeStrings = new List<nint>();
        try
        {
            var media = plan.Media ?? throw new InvalidOperationException(
                "The recording media configuration is required.");
            var hasDiscoveredSource = plan.Signal.HasDiscoveredSourceIdentity;
            var layout = plan.VideoLayout.CurrentLayout;
            var config = new NativeSessionConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeSessionConfigV1>()),
                AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
                TemporaryOutputPathUtf8 = AllocateUtf8(
                    plan.Output.TemporaryPath,
                    nativeStrings),
                Width = checked((uint)layout.OutputCanvas.Width),
                Height = checked((uint)layout.OutputCanvas.Height),
                FramesPerSecondNumerator = checked((uint)plan.FrameRate.Value),
                FramesPerSecondDenominator = 1,
                StartedAtUnixMillisecondsUtc = plan.StartedAt.UtcStartedAt
                    .ToUnixTimeMilliseconds(),
                Encoder = ToNativeEncoder(plan.Encoder),
                Reserved = 0,
                SourceWidth = checked((uint)layout.Source.Width),
                SourceHeight = checked((uint)layout.Source.Height),
                DestinationX = checked((uint)layout.Placement.OffsetX),
                DestinationY = checked((uint)layout.Placement.OffsetY),
                DestinationWidth = checked((uint)layout.Placement.Width),
                DestinationHeight = checked((uint)layout.Placement.Height),
                CanvasBackground = ToNativeBackground(layout.Background),
                Rotation = ToNativeRotation(layout.Rotation),
                AudioRouting = ToNativeAudioRouting(media.AudioRouting),
                QualityPreset = ToNativeQualityPreset(media.QualityPreset),
                DesktopEndpointIdUtf8 = AllocateUtf8(
                    media.DesktopEndpointId,
                    nativeStrings),
                MicrophoneEndpointIdUtf8 = AllocateUtf8(
                    media.MicrophoneEndpointId,
                    nativeStrings),
                DesktopGainDb = media.DesktopGainDb,
                MicrophoneGainDb = media.MicrophoneGainDb,
                SpoutSenderIdentityUtf8 = AllocateUtf8(
                    hasDiscoveredSource
                        ? plan.Signal.SenderId
                        : media.SpoutSenderIdentity,
                    nativeStrings),
                SpoutAdapterLuid = hasDiscoveredSource
                    ? plan.Signal.AdapterLuid
                    : media.SpoutAdapterLuid,
                EncoderAdapterLuid = hasDiscoveredSource
                    ? plan.Signal.AdapterLuid
                    : media.EncoderAdapterLuid,
                GpuIdentityUtf8 = AllocateUtf8(
                    hasDiscoveredSource
                        ? plan.Signal.GpuIdentity
                        : media.GpuIdentity,
                    nativeStrings),
                ReservedV1 = 0,
                SourcePixelFormat = ToNativeSourcePixelFormat(
                    plan.Signal.PixelFormat),
                ReservedV2 = 0,
                EstimatedSourceFramesPerSecond =
                    plan.Signal.EstimatedSourceFramesPerSecond,
            };
            var nativeCallbacks = new NativeCallbacksV1
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeCallbacksV1>()),
                AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
                OnEvent = Marshal.GetFunctionPointerForDelegate(EventCallback),
                UserData = GCHandle.ToIntPtr(callbackHandle),
            };
            var createStatus = _library.CreateSession(
                ref config,
                ref nativeCallbacks,
                out var nativeSession);
            if (createStatus != NativeStatus.Ok)
            {
                throw StatusException(createStatus, "create");
            }

            var safeSession = new NativeSessionSafeHandle(
                nativeSession,
                _library);
            try
            {
                var startStatus = _library.StartSession(nativeSession);
                if (startStatus != NativeStatus.Ok)
                {
                    throw StatusException(startStatus, "start");
                }

                return Task.FromResult<INativeRecordingSession>(
                    new PInvokeNativeRecordingSession(
                        _library,
                        safeSession,
                        callbackState,
                        callbackHandle,
                        ReleaseSessionLease));
            }
            catch
            {
                safeSession.Dispose();
                throw;
            }
        }
        catch
        {
            if (callbackHandle.IsAllocated)
            {
                callbackHandle.Free();
            }

            throw;
        }
        finally
        {
            foreach (var nativeString in nativeStrings)
            {
                Marshal.FreeCoTaskMem(nativeString);
            }
        }
    }

    private static nint AllocateUtf8(string value, List<nint> allocations)
    {
        var nativeString = Marshal.StringToCoTaskMemUTF8(value);
        allocations.Add(nativeString);
        return nativeString;
    }

    private void AcquireSessionLease()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeSessionCount++;
        }
    }

    private void ReleaseSessionLease()
    {
        lock (_lifetimeGate)
        {
            _activeSessionCount--;
        }
    }

    private static void OnNativeEvent(nint userData, nint nativeEvent)
    {
        try
        {
            if (userData == 0 || nativeEvent == 0)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is NativeCallbackState state)
            {
                state.Process(Marshal.PtrToStructure<NativeEventV1>(nativeEvent));
            }
        }
        catch
        {
            // Managed failures must never unwind through the native callback.
        }
    }

    private static NativeRecordingException StatusException(
        NativeStatus status,
        string operation) =>
        new(new NativeRecordingFault(
            (int)status,
            $"Native recording {operation} failed with status {(int)status}."));

    private static NativeEncoderKind ToNativeEncoder(
        Domain.Encoding.EncoderKind encoder) => encoder switch
        {
            Domain.Encoding.EncoderKind.Nvenc => NativeEncoderKind.Nvenc,
            Domain.Encoding.EncoderKind.Amf => NativeEncoderKind.Amf,
            Domain.Encoding.EncoderKind.Qsv => NativeEncoderKind.Qsv,
            Domain.Encoding.EncoderKind.MediaFoundationSoftware =>
                NativeEncoderKind.MediaFoundationSoftware,
            _ => throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "The selected encoder kind is unsupported by the native ABI."),
        };

    private static NativeCanvasBackground ToNativeBackground(
        VideoCanvasBackground background) => background switch
        {
            VideoCanvasBackground.Black => NativeCanvasBackground.Black,
            _ => throw new ArgumentOutOfRangeException(
                nameof(background),
                background,
                "The canvas background is unsupported by the native ABI."),
        };

    private static NativeVideoRotation ToNativeRotation(
        VideoRotation rotation) => rotation switch
        {
            VideoRotation.None => NativeVideoRotation.None,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rotation),
                rotation,
                "The video rotation is unsupported by the native ABI."),
        };

    private static NativeAudioRouting ToNativeAudioRouting(
        AudioRouting routing) => routing switch
        {
            AudioRouting.Mixed => NativeAudioRouting.Mixed,
            AudioRouting.DesktopOnly => NativeAudioRouting.DesktopOnly,
            AudioRouting.MicOnly => NativeAudioRouting.MicOnly,
            AudioRouting.Muted => NativeAudioRouting.Muted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(routing),
                routing,
                "The audio routing is unsupported by the native ABI."),
        };

    private static NativeSourcePixelFormat ToNativeSourcePixelFormat(
        VideoPixelFormat pixelFormat) => pixelFormat switch
        {
            VideoPixelFormat.Bgra8 => NativeSourcePixelFormat.Bgra8,
            VideoPixelFormat.Rgba8 => NativeSourcePixelFormat.Rgba8,
            VideoPixelFormat.Nv12 => NativeSourcePixelFormat.Nv12,
            _ => throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                "The source pixel format is unsupported by the native ABI."),
        };

    private static NativeQualityPreset ToNativeQualityPreset(
        VideoQualityPreset preset) => preset switch
        {
            VideoQualityPreset.Standard => NativeQualityPreset.Standard,
            VideoQualityPreset.High => NativeQualityPreset.High,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "The quality preset is unsupported by the native ABI."),
        };
}
