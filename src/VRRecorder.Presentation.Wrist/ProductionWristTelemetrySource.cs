using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Presentation.Wrist;

public sealed class ProductionWristTelemetrySource
    : IWristTelemetrySource,
      IRecordingMediaEventSink,
      IAudioSessionEventSink
{
    private readonly object _gate = new();
    private readonly IMonotonicClock _clock;
    private RecordingMediaProfile? _profile;
    private RecordingSessionStatistics? _statistics;
    private RecordingAudioControlState? _audioControl;
    private MonotonicTimestamp? _recordingStarted;
    private WristSignalHealth _desktopAudio = WristSignalHealth.Available;
    private WristSignalHealth _microphone = WristSignalHealth.Available;

    public ProductionWristTelemetrySource(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public WristTelemetrySnapshot? Capture(
        RecorderStatusSnapshot status,
        OverlayPlacementMode placementMode,
        IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(nameof(placementMode));
        }

        lock (_gate)
        {
            if (status.AudioControlState is not null)
            {
                _audioControl = status.AudioControlState;
            }
            if (!IsTelemetryState(status.State) || _profile is null)
            {
                if (!IsRecordingLike(status.State))
                {
                    _recordingStarted = null;
                    _statistics = null;
                    _audioControl = null;
                }
                return null;
            }

            var now = _clock.Now;
            _recordingStarted ??= now;
            var elapsed = now.Elapsed - _recordingStarted.Value.Elapsed;
            var actualFramesPerSecond = ResolveActualFramesPerSecond(elapsed);
            var desktop = ResolveAudioHealth(
                AudioInput.Desktop,
                _desktopAudio,
                _audioControl);
            var microphone = ResolveAudioHealth(
                AudioInput.Microphone,
                _microphone,
                _audioControl);
            var spout = status.State is RecorderState.SignalLost or
                RecorderState.NoSignal
                ? WristSignalHealth.Unavailable
                : WristSignalHealth.Available;
            var alerts = CreateAlerts(
                status.State,
                spout,
                desktop,
                microphone,
                localizer);
            return new WristTelemetrySnapshot(
                elapsed,
                _profile.OutputWidth,
                _profile.OutputHeight,
                _profile.OutputFramesPerSecond,
                actualFramesPerSecond,
                spout,
                desktop,
                microphone,
                EncoderDisplayName(_profile.Encoder),
                placementMode,
                alerts);
        }
    }

    void IRecordingMediaEventSink.Publish(RecordingMediaProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.OutputWidth is < 1 or > 16_384 ||
            profile.OutputHeight is < 1 or > 16_384 ||
            profile.OutputFramesPerSecond is < 1 or > 1_000 ||
            !double.IsFinite(profile.EstimatedSourceFramesPerSecond) ||
            profile.EstimatedSourceFramesPerSecond is < 0 or > 1_000 ||
            !Enum.IsDefined(profile.Encoder))
        {
            throw new ArgumentException(
                "The recording media profile cannot be shown on the wrist.",
                nameof(profile));
        }
        lock (_gate)
        {
            _profile = profile;
            _statistics = null;
            _desktopAudio = WristSignalHealth.Available;
            _microphone = WristSignalHealth.Available;
        }
    }

    void IRecordingMediaEventSink.Publish(
        RecordingSessionStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        lock (_gate)
        {
            _statistics = statistics;
        }
    }

    void IAudioSessionEventSink.Publish(AudioSessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        lock (_gate)
        {
            SetAudioHealth(warning.Input, WristSignalHealth.Unavailable);
        }
    }

    void IAudioSessionEventSink.Publish(AudioSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Kind != AudioSessionStatusKind.InputRecovered)
        {
            return;
        }
        lock (_gate)
        {
            SetAudioHealth(status.Input, WristSignalHealth.Available);
        }
    }

    private static bool IsTelemetryState(RecorderState state) =>
        IsRecordingLike(state) ||
        state is RecorderState.Faulted or RecorderState.ComplianceFault;

    private static bool IsRecordingLike(RecorderState state) =>
        state is RecorderState.Recording or RecorderState.SignalLost or
        RecorderState.Stopping;

    private double ResolveActualFramesPerSecond(TimeSpan elapsed)
    {
        if (_statistics is not null && elapsed > TimeSpan.Zero)
        {
            var measured = _statistics.MuxedVideoPacketCount /
                elapsed.TotalSeconds;
            if (double.IsFinite(measured) && measured is >= 0 and <= 1_000)
            {
                return measured;
            }
        }
        return _profile!.EstimatedSourceFramesPerSecond;
    }

    private static WristSignalHealth ResolveAudioHealth(
        AudioInput input,
        WristSignalHealth current,
        RecordingAudioControlState? control)
    {
        if (control is null || control.MuteAll ||
            (input == AudioInput.Desktop && !control.DesktopIncluded) ||
            (input == AudioInput.Microphone && !control.MicrophoneIncluded))
        {
            return WristSignalHealth.NotApplicable;
        }
        return current;
    }

    private static List<WristAlertSnapshot> CreateAlerts(
        RecorderState state,
        WristSignalHealth spout,
        WristSignalHealth desktop,
        WristSignalHealth microphone,
        IUiLocalizer localizer)
    {
        var alerts = new List<WristAlertSnapshot>(3);
        if (spout == WristSignalHealth.Unavailable)
        {
            alerts.Add(new WristAlertSnapshot(
                "signal.spout.unavailable",
                WristAlertSeverity.Warning,
                localizer.Resolve("state.signal-lost.label")));
        }
        if (desktop == WristSignalHealth.Unavailable)
        {
            alerts.Add(new WristAlertSnapshot(
                "signal.desktop-audio.unavailable",
                WristAlertSeverity.Warning,
                localizer.Resolve("telemetry.desktop-audio.unavailable")));
        }
        if (microphone == WristSignalHealth.Unavailable)
        {
            alerts.Add(new WristAlertSnapshot(
                "signal.microphone.unavailable",
                WristAlertSeverity.Warning,
                localizer.Resolve("telemetry.microphone.unavailable")));
        }
        if (state is RecorderState.Faulted or RecorderState.ComplianceFault)
        {
            alerts.Add(new WristAlertSnapshot(
                "recorder.fault",
                WristAlertSeverity.Fault,
                localizer.Resolve(state == RecorderState.Faulted
                    ? "state.faulted.label"
                    : "state.compliance-fault.label")));
        }
        return alerts;
    }

    private void SetAudioHealth(AudioInput input, WristSignalHealth health)
    {
        switch (input)
        {
            case AudioInput.Desktop:
                _desktopAudio = health;
                break;
            case AudioInput.Microphone:
                _microphone = health;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    private static string EncoderDisplayName(EncoderKind encoder) =>
        encoder switch
        {
            EncoderKind.Nvenc => "NVENC",
            EncoderKind.Amf => "AMF",
            EncoderKind.Qsv => "QSV",
            EncoderKind.MediaFoundationSoftware => "MF Software",
            _ => throw new ArgumentOutOfRangeException(nameof(encoder)),
        };
}
