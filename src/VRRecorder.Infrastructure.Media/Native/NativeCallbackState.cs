using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeCallbackState
{
    private readonly object _gate = new();
    private readonly NativeRecordingCallbacks _callbacks;
    private readonly RecordingPlan _plan;
    private ulong _lastSequence;
    private bool _terminal;

    public NativeCallbackState(
        RecordingPlan plan,
        NativeRecordingCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(callbacks);
        _plan = plan;
        _callbacks = callbacks;
    }

    public TaskCompletionSource<RecordingStopResult> Stopped { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void Process(NativeEventV1 nativeEvent)
    {
        Action? firstPacket = null;
        NativeRecordingFault? fault = null;
        RecordingStopResult? stopped = null;
        lock (_gate)
        {
            if (_terminal ||
                nativeEvent.StructSize < Marshal.SizeOf<NativeEventV1>() ||
                nativeEvent.AbiVersion != NativeAbiLibrary.SupportedAbiVersion ||
                nativeEvent.Sequence <= _lastSequence)
            {
                return;
            }

            _lastSequence = nativeEvent.Sequence;
            switch (nativeEvent.Kind)
            {
                case NativeEventKind.FirstVideoPacketMuxed:
                    firstPacket = _callbacks.FirstVideoPacketMuxed;
                    break;
                case NativeEventKind.Stopped:
                    _terminal = true;
                    stopped = new RecordingStopResult(
                        _plan.Output,
                        checked((long)nativeEvent.VideoPacketCount),
                        checked((long)nativeEvent.AudioPacketCount));
                    break;
                case NativeEventKind.Faulted:
                    _terminal = true;
                    fault = new NativeRecordingFault(
                        (int)nativeEvent.Status,
                        Marshal.PtrToStringUTF8(nativeEvent.MessageUtf8) ??
                        "Native recording failed.");
                    break;
                default:
                    return;
            }
        }

        if (firstPacket is not null)
        {
            firstPacket();
        }

        if (stopped is not null)
        {
            Stopped.TrySetResult(stopped);
        }

        if (fault is not null)
        {
            _callbacks.Faulted(fault);
            Stopped.TrySetException(new NativeRecordingException(fault));
        }
    }
}
