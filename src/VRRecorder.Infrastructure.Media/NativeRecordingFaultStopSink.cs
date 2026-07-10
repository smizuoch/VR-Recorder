using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingFaultStopSink
    : INativeRecordingRuntimeFaultSink
{
    private readonly object _gate = new();
    private readonly Dictionary<RecordingHandle, Task> _dispatches = [];
    private IStopRequestSink? _stopRequests;
    private NativeRecordingFaultStopFailure? _lastFailure;

    public NativeRecordingFaultStopFailure? LastFailure
    {
        get
        {
            lock (_gate)
            {
                return _lastFailure;
            }
        }
    }

    public void Bind(IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(stopRequests);
        lock (_gate)
        {
            if (_stopRequests is not null)
            {
                throw new InvalidOperationException(
                    "The native recording fault stop sink is already bound.");
            }

            _stopRequests = stopRequests;
        }
    }

    public void Report(
        RecordingHandle handle,
        NativeRecordingFault fault)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(fault);
        IStopRequestSink stopRequests;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            stopRequests = _stopRequests ??
                throw new InvalidOperationException(
                    "The native recording fault stop sink is not bound.");
            if (_dispatches.ContainsKey(handle))
            {
                return;
            }

            _dispatches.Add(handle, completion.Task);
        }

        _ = DispatchStopAsync(
            stopRequests,
            handle,
            fault,
            completion);
    }

    public Task WaitForDispatchAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Task dispatch;
        lock (_gate)
        {
            if (!_dispatches.TryGetValue(handle, out dispatch!))
            {
                throw new InvalidOperationException(
                    $"No native recording fault was reported for {handle.Id}.");
            }
        }

        return cancellationToken.CanBeCanceled
            ? dispatch.WaitAsync(cancellationToken)
            : dispatch;
    }

    private async Task DispatchStopAsync(
        IStopRequestSink stopRequests,
        RecordingHandle handle,
        NativeRecordingFault fault,
        TaskCompletionSource completion)
    {
        try
        {
            await stopRequests
                .RequestStopAsync(
                    new RecordingStopRequest(
                        handle,
                        RecordingStopReason.EncoderFailure),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _lastFailure = new NativeRecordingFaultStopFailure(
                    handle,
                    fault,
                    exception);
            }
        }
        finally
        {
            completion.TrySetResult();
        }
    }
}
