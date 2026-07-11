using System.Runtime.InteropServices;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeCallbackState
{
    private const int RequiredAudioChannels = 2;
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
        AudioSessionStatus? audioStatus = null;
        AudioSessionWarning? audioWarning = null;
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
                    var videoPacketCount = checked(
                        (long)nativeEvent.VideoPacketCount);
                    stopped = new RecordingStopResult(
                        _plan.Output,
                        videoPacketCount,
                        checked((long)nativeEvent.AudioPacketCount),
                        CreateMediaExpectation(videoPacketCount));
                    break;
                case NativeEventKind.Faulted:
                    _terminal = true;
                    fault = new NativeRecordingFault(
                        (int)nativeEvent.Status,
                        Marshal.PtrToStringUTF8(nativeEvent.MessageUtf8) ??
                        "Native recording failed.");
                    break;
                case NativeEventKind.DesktopAudioDeviceLost:
                    audioWarning = CreateAudioWarning(
                        nativeEvent,
                        Domain.Audio.AudioInput.Desktop);
                    break;
                case NativeEventKind.DesktopAudioDeviceRecovered:
                    audioStatus = CreateAudioStatus(
                        nativeEvent,
                        Domain.Audio.AudioInput.Desktop);
                    break;
                case NativeEventKind.MicrophoneAudioDeviceLost:
                    audioWarning = CreateAudioWarning(
                        nativeEvent,
                        Domain.Audio.AudioInput.Microphone);
                    break;
                case NativeEventKind.MicrophoneAudioDeviceRecovered:
                    audioStatus = CreateAudioStatus(
                        nativeEvent,
                        Domain.Audio.AudioInput.Microphone);
                    break;
                default:
                    return;
            }
        }

        if (firstPacket is not null)
        {
            firstPacket();
        }

        if (audioWarning is not null)
        {
            _callbacks.AudioWarning?.Invoke(audioWarning);
        }

        if (audioStatus is not null)
        {
            _callbacks.AudioStatus?.Invoke(audioStatus);
        }

        if (stopped is not null)
        {
            Stopped.TrySetResult(stopped);
        }

        if (fault is not null)
        {
            Stopped.TrySetException(new NativeRecordingException(fault));
            _callbacks.Faulted(fault);
        }
    }

    private static AudioSessionWarning? CreateAudioWarning(
        NativeEventV1 nativeEvent,
        Domain.Audio.AudioInput input) =>
        HasValidAudioPayload(nativeEvent)
            ? new AudioSessionWarning(
                AudioSessionWarningKind.InputUnavailable,
                input,
                checked((long)nativeEvent.AudioPacketCount))
            : null;

    private static AudioSessionStatus? CreateAudioStatus(
        NativeEventV1 nativeEvent,
        Domain.Audio.AudioInput input) =>
        HasValidAudioPayload(nativeEvent)
            ? new AudioSessionStatus(
                AudioSessionStatusKind.InputRecovered,
                input,
                checked((long)nativeEvent.AudioPacketCount))
            : null;

    private static bool HasValidAudioPayload(NativeEventV1 nativeEvent) =>
        nativeEvent.Status == NativeStatus.Ok &&
        nativeEvent.VideoPacketCount == 0 &&
        nativeEvent.MessageUtf8 == 0 &&
        nativeEvent.AudioPacketCount <= long.MaxValue;

    private RecordingMediaExpectation CreateMediaExpectation(
        long videoPacketCount)
    {
        var output = _plan.VideoLayout.OutputCanvas;
        return new RecordingMediaExpectation(
            output.Width,
            output.Height,
            _plan.FrameRate.Value,
            AudioSessionService.RequiredSampleRate,
            RequiredAudioChannels,
            TimeSpan.FromSeconds(
                (double)videoPacketCount / _plan.FrameRate.Value));
    }
}
