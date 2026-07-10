using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;
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
        var temporaryPath = Marshal.StringToCoTaskMemUTF8(
            plan.Output.TemporaryPath);
        try
        {
            var config = new NativeSessionConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeSessionConfigV1>()),
                AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
                TemporaryOutputPathUtf8 = temporaryPath,
                Width = checked((uint)plan.VideoLayout.OutputCanvas.Width),
                Height = checked((uint)plan.VideoLayout.OutputCanvas.Height),
                FramesPerSecondNumerator = checked((uint)plan.FrameRate.Value),
                FramesPerSecondDenominator = 1,
                StartedAtUnixMillisecondsUtc = plan.StartedAt.UtcStartedAt
                    .ToUnixTimeMilliseconds(),
                Encoder = ToNativeEncoder(plan.Encoder),
                Reserved = 0,
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
            Marshal.FreeCoTaskMem(temporaryPath);
        }
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
}
