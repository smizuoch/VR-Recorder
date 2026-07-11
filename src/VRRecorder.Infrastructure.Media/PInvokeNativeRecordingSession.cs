using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.Infrastructure.Media;

internal sealed class PInvokeNativeRecordingSession : INativeRecordingSession
{
    private readonly object _gate = new();
    private readonly NativeAbiLibrary _library;
    private readonly NativeSessionSafeHandle _session;
    private readonly NativeCallbackState _callbackState;
    private readonly Action _releaseBackendLease;
    private GCHandle _callbackHandle;
    private Task<RecordingStopResult>? _stopTask;
    private NativeRecordingSessionStatistics? _finalStatistics;
    private NativeRecordingFault? _finalStatisticsFault;
    private int _released;

    public PInvokeNativeRecordingSession(
        NativeAbiLibrary library,
        NativeSessionSafeHandle session,
        NativeCallbackState callbackState,
        GCHandle callbackHandle,
        Action releaseBackendLease)
    {
        ArgumentNullException.ThrowIfNull(releaseBackendLease);
        _library = library;
        _session = session;
        _callbackState = callbackState;
        _callbackHandle = callbackHandle;
        _releaseBackendLease = releaseBackendLease;
        Id = $"native-{Guid.NewGuid():N}";
    }

    public string Id { get; }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_released != 0)
            {
                return Task.CompletedTask;
            }

            var status = _library.AbortSession(_session.DangerousGetHandle());
            ReleaseCore();
            if (status != NativeStatus.Ok)
            {
                throw StatusException(status, "abort");
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateVideoLayoutAsync(
        RecordingVideoLayout layout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfReleased();
            var nativeLayout = new NativeVideoLayoutV1
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeVideoLayoutV1>()),
                AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
                SourceWidth = checked((uint)layout.Source.Width),
                SourceHeight = checked((uint)layout.Source.Height),
                CanvasWidth = checked((uint)layout.OutputCanvas.Width),
                CanvasHeight = checked((uint)layout.OutputCanvas.Height),
                DestinationX = checked((uint)layout.Placement.OffsetX),
                DestinationY = checked((uint)layout.Placement.OffsetY),
                DestinationWidth = checked((uint)layout.Placement.Width),
                DestinationHeight = checked((uint)layout.Placement.Height),
                CanvasBackground = ToNativeBackground(layout.Background),
                Rotation = ToNativeRotation(layout.Rotation),
            };
            var status = _library.UpdateVideoLayout(
                _session.DangerousGetHandle(),
                ref nativeLayout);
            if (status != NativeStatus.Ok)
            {
                throw StatusException(status, "update video layout");
            }
        }

        return Task.CompletedTask;
    }

    public Task<NativeRecordingSessionStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_released != 0)
            {
                if (_finalStatistics is not null)
                {
                    return Task.FromResult(_finalStatistics);
                }

                if (_finalStatisticsFault is not null)
                {
                    throw new NativeRecordingException(_finalStatisticsFault);
                }

                throw new InvalidOperationException(
                    "Final native recording statistics are unavailable.");
            }

            return Task.FromResult(QueryStatisticsCore());
        }
    }

    public Task<RecordingStopResult> StopAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfReleased();
            return _stopTask = StopCoreAsync();
        }
    }

    private async Task<RecordingStopResult> StopCoreAsync()
    {
        try
        {
            var status = _library.RequestStop(
                _session.DangerousGetHandle());
            if (status != NativeStatus.Ok)
            {
                throw StatusException(status, "request stop");
            }

            var result = await _callbackState.Stopped.Task.ConfigureAwait(false);
            lock (_gate)
            {
                try
                {
                    _finalStatistics = QueryStatisticsCore();
                }
                catch (NativeRecordingException exception)
                {
                    _finalStatisticsFault = exception.Fault;
                }
            }

            return result with
            {
                Statistics = _finalStatistics is null
                    ? null
                    : ToRecordingStatistics(_finalStatistics),
            };
        }
        finally
        {
            Release();
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            ReleaseCore();
        }
    }

    private void ReleaseCore()
    {
        if (_released != 0)
        {
            return;
        }

        _released = 1;
        try
        {
            _session.Dispose();
        }
        finally
        {
            try
            {
                if (_callbackHandle.IsAllocated)
                {
                    _callbackHandle.Free();
                }
            }
            finally
            {
                _releaseBackendLease();
            }
        }
    }

    private NativeRecordingSessionStatistics QueryStatisticsCore()
    {
        var statistics = new NativeSessionStatisticsV1
        {
            StructSize = checked(
                (uint)Marshal.SizeOf<NativeSessionStatisticsV1>()),
            AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
        };
        var status = _library.GetStatistics(
            _session.DangerousGetHandle(),
            ref statistics);
        if (status != NativeStatus.Ok)
        {
            throw StatusException(status, "query statistics");
        }

        try
        {
            return new NativeRecordingSessionStatistics(
                statistics.SourceVideoFrameCount,
                statistics.MuxedVideoPacketCount,
                statistics.MuxedAudioPacketCount,
                statistics.DroppedSourceVideoFrameCount,
                statistics.DuplicatedOutputVideoFrameCount,
                Microseconds(statistics.LatestEncodeLatencyMicroseconds),
                Microseconds(statistics.MaximumEncodeLatencyMicroseconds),
                Microseconds(statistics.AudioVideoOffsetMicroseconds));
        }
        catch (OverflowException exception)
        {
            throw new NativeRecordingException(
                new NativeRecordingFault(
                    (int)NativeStatus.InternalError,
                    "Native recording statistics exceeded the supported range."),
                exception);
        }
    }

    private static RecordingSessionStatistics ToRecordingStatistics(
        NativeRecordingSessionStatistics statistics) =>
        new(
            statistics.SourceVideoFrameCount,
            statistics.MuxedVideoPacketCount,
            statistics.MuxedAudioPacketCount,
            statistics.DroppedSourceVideoFrameCount,
            statistics.DuplicatedOutputVideoFrameCount,
            statistics.LatestEncodeLatency,
            statistics.MaximumEncodeLatency,
            statistics.AudioVideoOffset);

    private void ThrowIfReleased()
    {
        if (_released != 0)
        {
            throw new InvalidOperationException(
                "The native recording session is no longer active.");
        }
    }

    private static TimeSpan Microseconds(ulong value) =>
        TimeSpan.FromTicks(checked((long)value * 10));

    private static TimeSpan Microseconds(long value) =>
        TimeSpan.FromTicks(checked(value * 10));

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

    private static NativeRecordingException StatusException(
        NativeStatus status,
        string operation) =>
        new(new NativeRecordingFault(
            (int)status,
            $"Native recording {operation} failed with status {(int)status}."));
}
