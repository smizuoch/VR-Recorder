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
    private GCHandle _callbackHandle;
    private Task<RecordingStopResult>? _stopTask;
    private int _released;

    public PInvokeNativeRecordingSession(
        NativeAbiLibrary library,
        NativeSessionSafeHandle session,
        NativeCallbackState callbackState,
        GCHandle callbackHandle)
    {
        _library = library;
        _session = session;
        _callbackState = callbackState;
        _callbackHandle = callbackHandle;
        Id = $"native-{Guid.NewGuid():N}";
    }

    public string Id { get; }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        var status = _library.AbortSession(_session.DangerousGetHandle());
        Release();
        if (status != NativeStatus.Ok)
        {
            throw StatusException(status, "abort");
        }

        return Task.CompletedTask;
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

            return await _callbackState.Stopped.Task.ConfigureAwait(false);
        }
        finally
        {
            Release();
        }
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _session.Dispose();
        if (_callbackHandle.IsAllocated)
        {
            _callbackHandle.Free();
        }
    }

    private static NativeRecordingException StatusException(
        NativeStatus status,
        string operation) =>
        new(new NativeRecordingFault(
            (int)status,
            $"Native recording {operation} failed with status {(int)status}."));
}
