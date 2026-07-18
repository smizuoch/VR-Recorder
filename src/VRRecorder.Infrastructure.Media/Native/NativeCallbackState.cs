using System.Runtime.InteropServices;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Video;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeCallbackState
{
    private const int RequiredAudioChannels = 2;
    private readonly object _gate = new();
    private readonly NativeRecordingCallbacks _callbacks;
    private readonly RecordingPlan _plan;
    private ulong _lastSequence;
    private NativeRecordingFault? _terminalFault;
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

    public NativeRecordingFault? TerminalFault
    {
        get
        {
            lock (_gate)
            {
                return _terminalFault;
            }
        }
    }

    public void Process(NativeEventV1 nativeEvent)
    {
        Action? firstPacket = null;
        AudioSessionStatus? audioStatus = null;
        AudioSessionWarning? audioWarning = null;
        NativeAvDriftEvent? avDrift = null;
        RecordingAudioBufferHealthEvent? audioBufferHealth = null;
        NativeRecordingFault? fault = null;
        NativeRecordingFault? videoEncoderFailure = null;
        VideoGeometry? stableVideoGeometry = null;
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
                        "Native recording failed.",
                        DecodeFaultSource(nativeEvent.VideoPacketCount));
                    _terminalFault = fault;
                    break;
                case NativeEventKind.VideoEncoderFailedPartReady:
                    videoEncoderFailure = new NativeRecordingFault(
                        (int)nativeEvent.Status,
                        Marshal.PtrToStringUTF8(nativeEvent.MessageUtf8) ??
                        "Native video encoder failed after sealing the current part.",
                        NativeRecordingFaultSource.VideoEncoder);
                    break;
                case NativeEventKind.VideoGeometryStable:
                    stableVideoGeometry = CreateStableVideoGeometry(
                        nativeEvent);
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
                case NativeEventKind.AudioVideoDriftExceeded:
                    avDrift = CreateAvDrift(nativeEvent);
                    break;
                case NativeEventKind.DesktopAudioBufferUnderrun:
                    audioBufferHealth = CreateAudioBufferHealth(
                        nativeEvent,
                        Domain.Audio.AudioInput.Desktop,
                        AudioBufferHealthKind.Underrun);
                    break;
                case NativeEventKind.DesktopAudioBufferOverrun:
                    audioBufferHealth = CreateAudioBufferHealth(
                        nativeEvent,
                        Domain.Audio.AudioInput.Desktop,
                        AudioBufferHealthKind.Overrun);
                    break;
                case NativeEventKind.MicrophoneAudioBufferUnderrun:
                    audioBufferHealth = CreateAudioBufferHealth(
                        nativeEvent,
                        Domain.Audio.AudioInput.Microphone,
                        AudioBufferHealthKind.Underrun);
                    break;
                case NativeEventKind.MicrophoneAudioBufferOverrun:
                    audioBufferHealth = CreateAudioBufferHealth(
                        nativeEvent,
                        Domain.Audio.AudioInput.Microphone,
                        AudioBufferHealthKind.Overrun);
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

        if (avDrift is not null)
        {
            _callbacks.AvDrift?.Invoke(avDrift);
        }

        if (audioBufferHealth is not null)
        {
            _callbacks.AudioBufferHealth?.Invoke(audioBufferHealth);
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

        if (videoEncoderFailure is not null)
        {
            _callbacks.VideoEncoderFailed?.Invoke(videoEncoderFailure);
        }

        if (stableVideoGeometry is not null)
        {
            _callbacks.VideoGeometryStable?.Invoke(stableVideoGeometry);
        }
    }

    private static NativeRecordingFaultSource DecodeFaultSource(
        ulong source) => source switch
        {
            1 => NativeRecordingFaultSource.VideoEncoder,
            _ => NativeRecordingFaultSource.Unknown,
        };

    private static VideoGeometry? CreateStableVideoGeometry(
        NativeEventV1 nativeEvent)
    {
        if (nativeEvent.Status != NativeStatus.Ok ||
            nativeEvent.MessageUtf8 != 0 ||
            nativeEvent.AudioPacketCount is 0 or > int.MaxValue)
        {
            return null;
        }

        var width = nativeEvent.VideoPacketCount & uint.MaxValue;
        var pixelFormat = nativeEvent.VideoPacketCount >> 32;
        if (width is 0 or > int.MaxValue)
        {
            return null;
        }

        var managedPixelFormat = pixelFormat switch
        {
            (ulong)NativeSourcePixelFormat.Bgra8 => VideoPixelFormat.Bgra8,
            (ulong)NativeSourcePixelFormat.Rgba8 => VideoPixelFormat.Rgba8,
            (ulong)NativeSourcePixelFormat.Nv12 => VideoPixelFormat.Nv12,
            _ => (VideoPixelFormat?)null,
        };
        return managedPixelFormat is { } defined
            ? new VideoGeometry(
                checked((int)width),
                checked((int)nativeEvent.AudioPacketCount),
                defined)
            : null;
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

    private static RecordingAudioBufferHealthEvent? CreateAudioBufferHealth(
        NativeEventV1 nativeEvent,
        Domain.Audio.AudioInput input,
        AudioBufferHealthKind kind) =>
        HasValidAudioPayload(nativeEvent)
            ? new RecordingAudioBufferHealthEvent(
                input,
                kind,
                checked((long)nativeEvent.AudioPacketCount))
            : null;

    private static NativeAvDriftEvent? CreateAvDrift(
        NativeEventV1 nativeEvent)
    {
        if (nativeEvent.Status != NativeStatus.Ok ||
            nativeEvent.MessageUtf8 != 0 ||
            nativeEvent.VideoPacketCount > long.MaxValue ||
            nativeEvent.AudioPacketCount > long.MaxValue)
        {
            return null;
        }

        var video = checked((long)nativeEvent.VideoPacketCount);
        var audio = checked((long)nativeEvent.AudioPacketCount);
        var absolute = video >= audio ? video - audio : audio - video;
        return new NativeAvDriftEvent(
            TimeSpan.FromMicroseconds(video),
            TimeSpan.FromMicroseconds(audio),
            TimeSpan.FromMicroseconds(absolute));
    }

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
