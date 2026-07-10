using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed class VideoSignalSupervisor
{
    private readonly RecordingHandle _handle;
    private readonly IStopRequestSink _stopRequests;
    private readonly VideoSignalMonitor _monitor = new();
    private int _safeStopRequested;

    public VideoSignalSupervisor(
        RecordingHandle handle,
        IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(stopRequests);

        _handle = handle;
        _stopRequests = stopRequests;
    }

    public void ObserveFreshFrame(VideoFrameObservation frame) =>
        _monitor.ObserveFreshFrame(frame);

    public async Task<VideoSignalStatus> EvaluateAsync(
        MonotonicTimestamp now,
        CancellationToken cancellationToken)
    {
        var status = _monitor.Evaluate(now);
        if (status != VideoSignalStatus.SafeStop ||
            Interlocked.CompareExchange(ref _safeStopRequested, 1, 0) != 0)
        {
            return status;
        }

        try
        {
            await _stopRequests
                .RequestStopAsync(
                    new RecordingStopRequest(
                        _handle,
                        RecordingStopReason.SignalLost),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _safeStopRequested, 0);
            throw;
        }

        return status;
    }
}
