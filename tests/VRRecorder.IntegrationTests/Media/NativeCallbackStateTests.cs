using System.Runtime.InteropServices;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeCallbackStateTests
{
    [Fact]
    public void DeliversEveryNonterminalNativeEvent()
    {
        var firstPackets = 0;
        var warnings = new List<AudioSessionWarning>();
        var statuses = new List<AudioSessionStatus>();
        var drifts = new List<NativeAvDriftEvent>();
        var health = new List<RecordingAudioBufferHealthEvent>();
        var encoderFailures = new List<NativeRecordingFault>();
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(
                () => firstPackets++,
                _ => { },
                warnings.Add,
                statuses.Add,
                drifts.Add,
                health.Add,
                encoderFailures.Add));
        ulong sequence = 0;

        state.Process(Event(NativeEventKind.FirstVideoPacketMuxed, ++sequence));
        state.Process(Event(
            NativeEventKind.DesktopAudioDeviceLost,
            ++sequence,
            audioPacketCount: 10));
        state.Process(Event(
            NativeEventKind.DesktopAudioDeviceRecovered,
            ++sequence,
            audioPacketCount: 20));
        state.Process(Event(
            NativeEventKind.MicrophoneAudioDeviceLost,
            ++sequence,
            audioPacketCount: 30));
        state.Process(Event(
            NativeEventKind.MicrophoneAudioDeviceRecovered,
            ++sequence,
            audioPacketCount: 40));
        state.Process(Event(
            NativeEventKind.AudioVideoDriftExceeded,
            ++sequence,
            videoPacketCount: 100,
            audioPacketCount: 90));
        state.Process(Event(
            NativeEventKind.AudioVideoDriftExceeded,
            ++sequence,
            videoPacketCount: 90,
            audioPacketCount: 100));
        state.Process(Event(
            NativeEventKind.DesktopAudioBufferUnderrun,
            ++sequence,
            audioPacketCount: 50));
        state.Process(Event(
            NativeEventKind.DesktopAudioBufferOverrun,
            ++sequence,
            audioPacketCount: 60));
        state.Process(Event(
            NativeEventKind.MicrophoneAudioBufferUnderrun,
            ++sequence,
            audioPacketCount: 70));
        state.Process(Event(
            NativeEventKind.MicrophoneAudioBufferOverrun,
            ++sequence,
            audioPacketCount: 80));
        var message = Marshal.StringToCoTaskMemUTF8("encoder failed");
        try
        {
            state.Process(Event(
                NativeEventKind.VideoEncoderFailedPartReady,
                ++sequence,
                status: NativeStatus.InternalError,
                messageUtf8: message));
        }
        finally
        {
            Marshal.FreeCoTaskMem(message);
        }
        state.Process(Event(
            NativeEventKind.VideoEncoderFailedPartReady,
            ++sequence,
            status: NativeStatus.InternalError));

        Assert.Equal(1, firstPackets);
        Assert.Collection(
            warnings,
            warning => Assert.Equal(AudioInput.Desktop, warning.Input),
            warning => Assert.Equal(AudioInput.Microphone, warning.Input));
        Assert.Collection(
            statuses,
            status => Assert.Equal(AudioInput.Desktop, status.Input),
            status => Assert.Equal(AudioInput.Microphone, status.Input));
        Assert.All(drifts, drift => Assert.Equal(
            TimeSpan.FromMicroseconds(10),
            drift.AbsoluteDrift));
        Assert.Collection(
            health,
            item => Assert.Equal(
                (AudioInput.Desktop, AudioBufferHealthKind.Underrun),
                (item.Input, item.Kind)),
            item => Assert.Equal(
                (AudioInput.Desktop, AudioBufferHealthKind.Overrun),
                (item.Input, item.Kind)),
            item => Assert.Equal(
                (AudioInput.Microphone, AudioBufferHealthKind.Underrun),
                (item.Input, item.Kind)),
            item => Assert.Equal(
                (AudioInput.Microphone, AudioBufferHealthKind.Overrun),
                (item.Input, item.Kind)));
        Assert.Collection(
            encoderFailures,
            failure => Assert.Equal("encoder failed", failure.Message),
            failure => Assert.Contains(
                "Native video encoder failed",
                failure.Message));
    }

    [Fact]
    public async Task StopAndFaultCompleteExactlyOnceAndIgnoreLaterEvents()
    {
        var firstPackets = 0;
        var stoppedState = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(
                () => firstPackets++,
                _ => { }));
        stoppedState.Process(Event(
            NativeEventKind.Stopped,
            sequence: 1,
            videoPacketCount: 60,
            audioPacketCount: 90));
        stoppedState.Process(Event(
            NativeEventKind.FirstVideoPacketMuxed,
            sequence: 2));

        var result = await stoppedState.Stopped.Task;
        Assert.Equal(60, result.VideoPacketCount);
        Assert.Equal(90, result.AudioPacketCount);
        Assert.NotNull(result.MediaExpectation);
        Assert.Equal(
            TimeSpan.FromSeconds(2),
            result.MediaExpectation.ExpectedDuration);
        Assert.Equal(0, firstPackets);

        var faults = new List<NativeRecordingFault>();
        var faultedState = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(() => { }, faults.Add));
        faultedState.Process(Event(
            NativeEventKind.Faulted,
            sequence: 1,
            status: NativeStatus.InternalError));

        var exception = await Assert.ThrowsAsync<NativeRecordingException>(() =>
            faultedState.Stopped.Task);
        Assert.Contains("Native recording failed", exception.Message);
        Assert.Single(faults);
    }

    [Fact]
    public async Task FaultUsesNativeMessageWhenPresent()
    {
        NativeRecordingFault? observed = null;
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(() => { }, fault => observed = fault));
        var message = Marshal.StringToCoTaskMemUTF8("native detail");
        try
        {
            state.Process(Event(
                NativeEventKind.Faulted,
                sequence: 1,
                status: NativeStatus.InternalError,
                messageUtf8: message));
        }
        finally
        {
            Marshal.FreeCoTaskMem(message);
        }

        _ = await Assert.ThrowsAsync<NativeRecordingException>(() =>
            state.Stopped.Task);
        Assert.Equal("native detail", observed?.Message);
    }

    [Fact]
    public void DeliversStableVideoGeometryFromThePackedAbiPayload()
    {
        var geometries = new List<VideoGeometry>();
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(
                () => { },
                _ => { },
                VideoGeometryStable: geometries.Add));
        var packedFormatAndWidth =
            (ulong)NativeSourcePixelFormat.Rgba8 << 32 | 1_280UL;

        state.Process(Event(
            NativeEventKind.VideoGeometryStable,
            sequence: 1,
            videoPacketCount: packedFormatAndWidth,
            audioPacketCount: 720));

        var geometry = Assert.Single(geometries);
        Assert.Equal(1_280, geometry.Width);
        Assert.Equal(720, geometry.Height);
        Assert.Equal(VideoPixelFormat.Rgba8, geometry.PixelFormat);
    }

    [Fact]
    public void RejectsInvalidHeaderSequenceAndUnknownKind()
    {
        var delivered = 0;
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(() => delivered++, _ => { }));
        var tooSmall = Event(NativeEventKind.FirstVideoPacketMuxed, 1);
        tooSmall.StructSize--;
        state.Process(tooSmall);
        var wrongAbi = Event(NativeEventKind.FirstVideoPacketMuxed, 2);
        wrongAbi.AbiVersion++;
        state.Process(wrongAbi);
        state.Process(Event(NativeEventKind.FirstVideoPacketMuxed, 3));
        state.Process(Event(NativeEventKind.FirstVideoPacketMuxed, 3));
        state.Process(Event((NativeEventKind)999, 4));

        Assert.Equal(1, delivered);
    }

    [Theory]
    [InlineData(InvalidAudioPayload.Status)]
    [InlineData(InvalidAudioPayload.VideoCount)]
    [InlineData(InvalidAudioPayload.Message)]
    [InlineData(InvalidAudioPayload.AudioCountOverflow)]
    public void InvalidAudioPayloadIsIgnored(InvalidAudioPayload invalid)
    {
        var warnings = new List<AudioSessionWarning>();
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(
                () => { },
                _ => { },
                warnings.Add));
        var nativeEvent = Event(
            NativeEventKind.DesktopAudioDeviceLost,
            sequence: 1,
            audioPacketCount: 1);
        nint message = 0;
        switch (invalid)
        {
            case InvalidAudioPayload.Status:
                nativeEvent.Status = NativeStatus.InternalError;
                break;
            case InvalidAudioPayload.VideoCount:
                nativeEvent.VideoPacketCount = 1;
                break;
            case InvalidAudioPayload.Message:
                message = Marshal.StringToCoTaskMemUTF8("unexpected");
                nativeEvent.MessageUtf8 = message;
                break;
            case InvalidAudioPayload.AudioCountOverflow:
                nativeEvent.AudioPacketCount = (ulong)long.MaxValue + 1;
                break;
            default:
                throw new InvalidOperationException(
                    "The invalid audio payload is not supported.");
        }

        try
        {
            state.Process(nativeEvent);
        }
        finally
        {
            if (message != 0)
            {
                Marshal.FreeCoTaskMem(message);
            }
        }

        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(InvalidDriftPayload.Status)]
    [InlineData(InvalidDriftPayload.Message)]
    [InlineData(InvalidDriftPayload.VideoCountOverflow)]
    [InlineData(InvalidDriftPayload.AudioCountOverflow)]
    public void InvalidDriftPayloadIsIgnored(InvalidDriftPayload invalid)
    {
        var drifts = new List<NativeAvDriftEvent>();
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(
                () => { },
                _ => { },
                AvDrift: drifts.Add));
        var nativeEvent = Event(
            NativeEventKind.AudioVideoDriftExceeded,
            sequence: 1,
            videoPacketCount: 1,
            audioPacketCount: 1);
        nint message = 0;
        switch (invalid)
        {
            case InvalidDriftPayload.Status:
                nativeEvent.Status = NativeStatus.InternalError;
                break;
            case InvalidDriftPayload.Message:
                message = Marshal.StringToCoTaskMemUTF8("unexpected");
                nativeEvent.MessageUtf8 = message;
                break;
            case InvalidDriftPayload.VideoCountOverflow:
                nativeEvent.VideoPacketCount = (ulong)long.MaxValue + 1;
                break;
            case InvalidDriftPayload.AudioCountOverflow:
                nativeEvent.AudioPacketCount = (ulong)long.MaxValue + 1;
                break;
            default:
                throw new InvalidOperationException(
                    "The invalid drift payload is not supported.");
        }

        try
        {
            state.Process(nativeEvent);
        }
        finally
        {
            if (message != 0)
            {
                Marshal.FreeCoTaskMem(message);
            }
        }

        Assert.Empty(drifts);
    }

    [Fact]
    public void StopPacketCountOverflowFailsClosed()
    {
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(() => { }, _ => { }));

        Assert.Throws<OverflowException>(() => state.Process(Event(
            NativeEventKind.Stopped,
            sequence: 1,
            videoPacketCount: (ulong)long.MaxValue + 1)));
    }

    [Fact]
    public void OptionalCallbacksMayBeOmittedAndInvalidPayloadsAreIgnored()
    {
        var state = new NativeCallbackState(
            Plan(),
            new NativeRecordingCallbacks(() => { }, _ => { }));

        state.Process(Event(
            NativeEventKind.DesktopAudioDeviceLost,
            sequence: 1,
            audioPacketCount: 1));
        state.Process(Event(
            NativeEventKind.DesktopAudioDeviceRecovered,
            sequence: 2,
            audioPacketCount: 2));
        state.Process(Event(
            NativeEventKind.AudioVideoDriftExceeded,
            sequence: 3,
            videoPacketCount: 3,
            audioPacketCount: 2));
        state.Process(Event(
            NativeEventKind.DesktopAudioBufferUnderrun,
            sequence: 4,
            audioPacketCount: 4));
        state.Process(Event(
            NativeEventKind.VideoEncoderFailedPartReady,
            sequence: 5,
            status: NativeStatus.InternalError));
        state.Process(Event(
            NativeEventKind.DesktopAudioDeviceRecovered,
            sequence: 6,
            status: NativeStatus.InternalError));
        state.Process(Event(
            NativeEventKind.DesktopAudioBufferUnderrun,
            sequence: 7,
            status: NativeStatus.InternalError));

        Assert.False(state.Stopped.Task.IsCompleted);
    }

    public enum InvalidAudioPayload
    {
        Status,
        VideoCount,
        Message,
        AudioCountOverflow,
    }

    public enum InvalidDriftPayload
    {
        Status,
        Message,
        VideoCountOverflow,
        AudioCountOverflow,
    }

    private static NativeEventV1 Event(
        NativeEventKind kind,
        ulong sequence,
        NativeStatus status = NativeStatus.Ok,
        ulong videoPacketCount = 0,
        ulong audioPacketCount = 0,
        nint messageUtf8 = 0) => new()
        {
            StructSize = checked((uint)Marshal.SizeOf<NativeEventV1>()),
            AbiVersion = NativeAbiLibrary.SupportedAbiVersion,
            Kind = kind,
            Status = status,
            Sequence = sequence,
            VideoPacketCount = videoPacketCount,
            AudioPacketCount = audioPacketCount,
            MessageUtf8 = messageUtf8,
        };

    private static RecordingPlan Plan()
    {
        var signal = new StableVideoSignal(
            "sender",
            adapterLuid: 1,
            "gpu",
            GpuVendor.Unknown,
            1280,
            720,
            VideoPixelFormat.Bgra8,
            30);
        return new RecordingPlan(
            signal,
            new PendingRecording(
                Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "recording.mp4")),
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30));
    }
}
